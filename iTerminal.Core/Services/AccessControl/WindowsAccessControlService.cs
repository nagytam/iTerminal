using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace iTerminal.Core.Services.AccessControl;

/// <summary>
/// NTFS access control lists through <see cref="FileSystemAclExtensions"/>. The list is shown as Explorer's Security
/// tab does: explicit entries first, inherited ones after, each as the preset its mask equals (Full control, Modify,
/// Read &amp; execute, Read, Write) or as "Special" when it is none of them.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAccessControlService : IAccessControlService
{
    private static readonly (string Name, FileSystemRights Rights)[] PresetTable =
    [
        ("Full control", FileSystemRights.FullControl),
        ("Modify", FileSystemRights.Modify),
        ("Read & execute", FileSystemRights.ReadAndExecute),
        ("Read", FileSystemRights.Read),
        ("Write", FileSystemRights.Write),
    ];

    /// <summary>The bits that make a preset what it is; the synchronise bit is on every rule Windows writes and says nothing.</summary>
    private const FileSystemRights Significant = ~FileSystemRights.Synchronize;

    public AclInfo Read(string path)
    {
        var (security, isFolder) = Load(path, AccessControlSections.Access | AccessControlSections.Owner);
        var entries = new List<AclEntry>();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(NTAccount)))
        {
            var preset = PresetFor(rule.FileSystemRights);
            entries.Add(new AclEntry(
                rule.IdentityReference.Value,
                rule.AccessControlType == AccessControlType.Allow ? AclKind.Allow : AclKind.Deny,
                preset ?? "Special (" + DescribeSpecial(rule.FileSystemRights) + ")",
                preset is not null,
                rule.IsInherited,
                Scope(rule, isFolder)));
        }

        // Explicit first, as Explorer lists them, then by name so the order does not move between reads.
        var ordered = entries
            .OrderBy(e => e.Inherited)
            .ThenBy(e => e.Kind)
            .ThenBy(e => e.Principal, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var owner = security.GetOwner(typeof(NTAccount))?.Value ?? "(unknown)";
        return new AclInfo(
            Path.GetFullPath(path),
            isFolder,
            owner,
            Group: null,
            ordered,
            Inherits: !security.AreAccessRulesProtected,
            CanChangeOwner: true,
            CanChangeInheritance: true,
            SupportsDeny: true,
            [.. PresetTable.Select(p => p.Name)]);
    }

    public void Set(string path, string principal, AclKind kind, string preset, bool inheritToChildren)
    {
        var rights = RightsFor(preset);
        var account = Resolve(principal);
        var (security, isFolder) = Load(path, AccessControlSections.Access);
        var inheritance = isFolder && inheritToChildren
            ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit
            : InheritanceFlags.None;
        var rule = new FileSystemAccessRule(account, rights, inheritance, PropagationFlags.None,
            kind == AclKind.Allow ? AccessControlType.Allow : AccessControlType.Deny);
        security.ModifyAccessRule(AccessControlModification.Set, rule, out _);
        Save(path, isFolder, security);
    }

    public void Remove(string path, string principal, AclKind kind)
    {
        var account = Resolve(principal);
        var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
        var (security, isFolder) = Load(path, AccessControlSections.Access);
        var type = kind == AclKind.Allow ? AccessControlType.Allow : AccessControlType.Deny;
        var removed = false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
        {
            if (rule.IdentityReference.Equals(sid) && rule.AccessControlType == type)
            {
                security.RemoveAccessRuleSpecific(rule);
                removed = true;
            }
        }

        if (!removed)
        {
            throw new ArgumentException($"{principal} has no explicit {kind.ToString().ToLowerInvariant()} entry here (an inherited one has to be removed from the folder it comes from).");
        }

        Save(path, isFolder, security);
    }

    public void SetInheritance(string path, bool inherit)
    {
        var (security, isFolder) = Load(path, AccessControlSections.Access);
        // Off keeps what was inherited as explicit entries: switching off must not lock everybody out.
        security.SetAccessRuleProtection(isProtected: !inherit, preserveInheritance: true);
        Save(path, isFolder, security);
    }

    public void SetOwner(string path, string? owner, string? group)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return;
        }

        var account = Resolve(owner);
        var (security, isFolder) = Load(path, AccessControlSections.Owner);
        security.SetOwner(account);
        Save(path, isFolder, security);
    }

    /// <summary>The rights a preset name stands for. Public so a test and the dialog agree on the names.</summary>
    internal static FileSystemRights RightsFor(string preset)
    {
        foreach (var (name, rights) in PresetTable)
        {
            if (string.Equals(name, preset, StringComparison.OrdinalIgnoreCase))
            {
                return rights;
            }
        }

        throw new ArgumentException($"\"{preset}\" is not one of: {string.Join(", ", PresetTable.Select(p => p.Name))}.");
    }

    internal static string? PresetFor(FileSystemRights rights)
    {
        var wanted = rights & Significant;
        // Generic bits arrive on a rule written by a program that did not map them: GENERIC_ALL is full control.
        if ((rights & (FileSystemRights)0x10000000) != 0)
        {
            return PresetTable[0].Name;
        }

        foreach (var (name, preset) in PresetTable)
        {
            if (wanted == (preset & Significant))
            {
                return name;
            }
        }

        return null;
    }

    private static string DescribeSpecial(FileSystemRights rights) =>
        (rights & Significant).ToString().Replace(", ", "|", StringComparison.Ordinal);

    private static string? Scope(FileSystemAccessRule rule, bool isFolder)
    {
        if (!isFolder)
        {
            return null;
        }

        var container = (rule.InheritanceFlags & InheritanceFlags.ContainerInherit) != 0;
        var objects = (rule.InheritanceFlags & InheritanceFlags.ObjectInherit) != 0;
        if (rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly))
        {
            return container && objects ? "subfolders, files"
                : container ? "subfolders"
                : objects ? "files"
                : null;
        }

        return container && objects ? "folder, subfolders, files"
            : container ? "folder, subfolders"
            : objects ? "folder, files"
            : "folder only";
    }

    private static NTAccount Resolve(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Give the user or group to change, such as Users or DOMAIN\\name.");
        }

        var account = new NTAccount(trimmed);
        try
        {
            account.Translate(typeof(SecurityIdentifier));
        }
        catch (IdentityNotMappedException)
        {
            throw new ArgumentException($"\"{trimmed}\" is not a user or group this computer knows.");
        }
        catch (SystemException exception) when (exception is ArgumentException or InvalidOperationException)
        {
            throw new ArgumentException($"\"{trimmed}\" is not a usable account name.");
        }

        return account;
    }

    private static (FileSystemSecurity Security, bool IsFolder) Load(string path, AccessControlSections sections)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (Directory.Exists(full))
        {
            return (new DirectoryInfo(full).GetAccessControl(sections), true);
        }

        if (File.Exists(full))
        {
            return (new FileInfo(full).GetAccessControl(sections), false);
        }

        throw new IOException($"{full} does not exist.");
    }

    private static void Save(string path, bool isFolder, FileSystemSecurity security)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (isFolder)
        {
            new DirectoryInfo(full).SetAccessControl((DirectorySecurity)security);
        }
        else
        {
            new FileInfo(full).SetAccessControl((FileSecurity)security);
        }
    }
}
