using iTerminal.Core.Services.Processes;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.AccessControl;

/// <summary>
/// POSIX ACLs through <c>getfacl</c> and <c>setfacl</c>, the owner and group through <c>chown</c>. The tools are the ones the
/// system's own <c>acl</c> package ships, run through <see cref="IExternalProcessRunner"/> with an argument list — nothing is
/// ever spliced into a command line. Without <c>getfacl</c> the permission bits still show (owner, group, others), read
/// from the file mode, with a note that named users and groups need the package.
///
/// <para>POSIX ACLs only grant: there is no deny entry, and nothing is inherited from the parent (a folder's
/// <em>default</em> entries are what new items start with, which is what "inherit" writes).</para>
/// </summary>
public sealed partial class UnixAccessControlService(IExternalProcessRunner runner) : IAccessControlService
{
    /// <summary>The rwx triples, strongest first.</summary>
    internal static readonly string[] PresetNames = ["rwx", "rw-", "r-x", "r--", "-wx", "-w-", "--x", "---"];

    public AclInfo Read(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var isFolder = Directory.Exists(full);
        if (!isFolder && !File.Exists(full))
        {
            throw new IOException($"{full} does not exist.");
        }

        var output = Run("getfacl", ["--absolute-names", "--", full]);
        if (output.ExitCode == -1)
        {
            return FromMode(full, isFolder, output.StandardError);
        }

        if (!output.Success)
        {
            throw new IOException(output.FirstErrorLine);
        }

        return Parse(full, isFolder, output.StandardOutput);
    }

    public void Set(string path, string principal, AclKind kind, string preset, bool inheritToChildren)
    {
        if (kind == AclKind.Deny)
        {
            throw new NotSupportedException("POSIX ACLs only grant access; there is no deny entry.");
        }

        if (!PresetNames.Contains(preset, StringComparer.Ordinal))
        {
            throw new ArgumentException($"\"{preset}\" is not a permission such as rwx, r-x or r--.");
        }

        var spec = Spec(principal) + ":" + preset;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        Check(Run("setfacl", ["-m", spec, "--", full]));
        if (inheritToChildren && Directory.Exists(full))
        {
            Check(Run("setfacl", ["-d", "-m", spec, "--", full]));
        }
    }

    public void Remove(string path, string principal, AclKind kind)
    {
        if (kind == AclKind.Deny)
        {
            throw new NotSupportedException("POSIX ACLs have no deny entries.");
        }

        var spec = Spec(principal);
        if (spec is "u::" or "g::" or "o::" or "m::")
        {
            throw new ArgumentException("The owner, the owning group, the mask and others always exist; change them instead of removing them.");
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        Check(Run("setfacl", ["-x", spec, "--", full]));
        if (Directory.Exists(full))
        {
            // A default entry for the same name goes too; there may be none, which setfacl says by failing, and that is fine.
            Run("setfacl", ["-d", "-x", spec, "--", full]);
        }
    }

    public void SetInheritance(string path, bool inherit) =>
        throw new NotSupportedException("A POSIX ACL has no inheritance switch; a folder's default entries are what new items start with.");

    public void SetOwner(string path, string? owner, string? group)
    {
        var name = string.IsNullOrWhiteSpace(owner) ? string.Empty : ValidName(owner);
        var groupName = string.IsNullOrWhiteSpace(group) ? string.Empty : ValidName(group);
        if (name.Length == 0 && groupName.Length == 0)
        {
            return;
        }

        var target = groupName.Length == 0 ? name : name + ":" + groupName;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        Check(Run("chown", [target, "--", full]));
    }

    private ProcessOutput Run(string tool, IReadOnlyList<string> arguments) =>
        runner.RunAsync(tool, arguments).GetAwaiter().GetResult();

    private static void Check(ProcessOutput output)
    {
        if (output.Success)
        {
            return;
        }

        var line = output.FirstErrorLine;
        if (output.ExitCode == -1)
        {
            throw new NotSupportedException(line + " — install the acl package (setfacl) to edit access lists.");
        }

        if (line.Contains("not permitted", StringComparison.OrdinalIgnoreCase) || line.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(line + " — only the owner (or root) may change this; restart as root with Ctrl+Shift+A.");
        }

        throw new IOException(line);
    }

    /// <summary>The <c>setfacl</c> entry spec, without the permissions, for what the dialog calls a principal.</summary>
    internal static string Spec(string principal)
    {
        var text = (principal ?? string.Empty).Trim();
        switch (text.ToLowerInvariant())
        {
            case "owner" or "user" or "user::":
                return "u::";
            case "group" or "group::":
                return "g::";
            case "other" or "others":
                return "o::";
            case "mask":
                return "m::";
        }

        if (text.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
        {
            return "u:" + ValidName(text[5..]);
        }

        if (text.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            return "g:" + ValidName(text[6..]);
        }

        // A bare name is a user; a group is said "group:name".
        return "u:" + ValidName(text);
    }

    private static string ValidName(string name)
    {
        var trimmed = name.Trim();
        if (!NamePattern().IsMatch(trimmed))
        {
            throw new ArgumentException($"\"{trimmed}\" is not a user or group name (letters, digits, . _ - only; numeric ids work too).");
        }

        return trimmed;
    }

    /// <summary>Parses <c>getfacl</c> output: the <c># owner:</c> and <c># group:</c> comments, then <c>tag:name:rwx</c> lines.</summary>
    internal static AclInfo Parse(string path, bool isFolder, string text)
    {
        string owner = "(unknown)", group = "(unknown)";
        var entries = new List<AclEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("# owner:", StringComparison.Ordinal))
            {
                owner = line["# owner:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("# group:", StringComparison.Ordinal))
            {
                group = line["# group:".Length..].Trim();
                continue;
            }

            if (line[0] == '#')
            {
                continue;
            }

            // "user:bob:rwx   #effective:r--" — the comment says what the mask leaves of it.
            var hash = line.IndexOf('#');
            var effective = string.Empty;
            if (hash >= 0)
            {
                effective = line[hash..].Replace("#effective:", string.Empty, StringComparison.Ordinal).Trim();
                line = line[..hash].Trim();
            }

            var isDefault = line.StartsWith("default:", StringComparison.Ordinal);
            if (isDefault)
            {
                line = line["default:".Length..];
            }

            var parts = line.Split(':');
            if (parts.Length != 3 || !IsPermission(parts[2]))
            {
                continue;
            }

            var principal = parts[0] switch
            {
                "user" when parts[1].Length == 0 => "owner",
                "user" => "user:" + parts[1],
                "group" when parts[1].Length == 0 => "group",
                "group" => "group:" + parts[1],
                "mask" => "mask",
                "other" => "other",
                _ => null,
            };
            if (principal is null)
            {
                continue;
            }

            var rights = parts[2] + (effective.Length > 0 && effective != parts[2] ? $" (effective {effective})" : string.Empty);
            entries.Add(new AclEntry(principal, AclKind.Allow, rights, IsPreset: true, Inherited: false,
                Scope: isDefault ? "default for new items" : null));
        }

        return new AclInfo(path, isFolder, owner, group, Order(entries), Inherits: false, CanChangeOwner: true, CanChangeInheritance: false,
            SupportsDeny: false, PresetNames);
    }

    private static List<AclEntry> Order(List<AclEntry> entries)
    {
        static int Rank(string principal) => principal switch
        {
            "owner" => 0,
            _ when principal.StartsWith("user:", StringComparison.Ordinal) => 1,
            "group" => 2,
            _ when principal.StartsWith("group:", StringComparison.Ordinal) => 3,
            "mask" => 4,
            _ => 5,
        };

        return [.. entries.OrderBy(e => e.Scope is not null).ThenBy(e => Rank(e.Principal)).ThenBy(e => e.Principal, StringComparer.Ordinal)];
    }

    private static bool IsPermission(string text) =>
        text.Length == 3 && text[0] is 'r' or '-' && text[1] is 'w' or '-' && text[2] is 'x' or '-';

    /// <summary>No <c>getfacl</c>: the nine permission bits are the whole list, and the owner and group come from <c>stat</c> where it can say.</summary>
    private AclInfo FromMode(string full, bool isFolder, string why)
    {
        string owner = "(unknown)", group = "(unknown)";
        var stat = Run("stat", ["-c", "%U:%G", "--", full]);
        if (stat.Success && stat.StandardOutput.Trim().Split(':') is [var u, var g])
        {
            (owner, group) = (u, g);
        }

        var mode = OperatingSystem.IsWindows() ? UnixFileMode.None : File.GetUnixFileMode(full);
        static string Triple(UnixFileMode mode, UnixFileMode r, UnixFileMode w, UnixFileMode x) =>
            ((mode & r) != 0 ? "r" : "-") + ((mode & w) != 0 ? "w" : "-") + ((mode & x) != 0 ? "x" : "-");
        AclEntry[] entries =
        [
            new("owner", AclKind.Allow, Triple(mode, UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute), true, false),
            new("group", AclKind.Allow, Triple(mode, UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute), true, false),
            new("other", AclKind.Allow, Triple(mode, UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute), true, false),
        ];
        return new AclInfo(full, isFolder, owner, group, entries, Inherits: false, CanChangeOwner: true, CanChangeInheritance: false,
            SupportsDeny: false, PresetNames,
            Note: "getfacl is not installed: showing the permission bits only (" + why.Trim() + "). Install the acl package for named users and groups.");
    }

    [GeneratedRegex(@"^[A-Za-z0-9_.][A-Za-z0-9_.\-$@]*$")]
    private static partial Regex NamePattern();
}
