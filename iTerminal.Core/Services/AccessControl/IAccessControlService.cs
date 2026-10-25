namespace iTerminal.Core.Services.AccessControl;

/// <summary>Whether an entry grants or refuses. POSIX ACLs only grant (<see cref="AclInfo.SupportsDeny"/>).</summary>
public enum AclKind
{
    Allow,
    Deny,
}

/// <summary>
/// One line of an access control list.
/// </summary>
/// <param name="Principal">Who it is for, as <see cref="IAccessControlService.Set"/> takes it back: an account on Windows
/// (<c>BUILTIN\Users</c>), <c>user:bob</c>, <c>group:staff</c>, <c>owner</c>, <c>group</c>, <c>mask</c> or <c>other</c> on Unix.</param>
/// <param name="Rights">What it grants or refuses: one of <see cref="AclInfo.Presets"/>, or a description of rights that
/// are none of them (<see cref="IsPreset"/> false), which can be removed but not written back.</param>
/// <param name="Inherited">It comes from the parent folder; it is shown but cannot be changed or removed here.</param>
/// <param name="Scope">Which items it reaches, when that is not just this one ("this folder and what is in it").</param>
public sealed record AclEntry(string Principal, AclKind Kind, string Rights, bool IsPreset, bool Inherited, string? Scope = null);

/// <summary>An entry's list, its owner, and what the system under it lets be changed.</summary>
/// <param name="Group">The owning group; null where the system has none (Windows keeps it in the list).</param>
/// <param name="Presets">The rights that can be written, strongest first; the dialog cycles through them.</param>
/// <param name="Note">Something the user has to know about what is shown, such as a missing tool.</param>
public sealed record AclInfo(
    string Path,
    bool IsFolder,
    string Owner,
    string? Group,
    IReadOnlyList<AclEntry> Entries,
    bool Inherits,
    bool CanChangeOwner,
    bool CanChangeInheritance,
    bool SupportsDeny,
    IReadOnlyList<string> Presets,
    string? Note = null);

/// <summary>
/// Reads and changes who may do what to a local file or folder: the NTFS access control list on Windows, the POSIX ACL
/// (<c>getfacl</c> / <c>setfacl</c>) and the owner and group (<c>chown</c>) elsewhere. Failures are
/// <see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>, <see cref="ArgumentException"/> or
/// <see cref="NotSupportedException"/>, each with a sentence a footer can show. Blocking by contract — a tool or a
/// directory security read — so a dialog calls it from a background task.
/// </summary>
public interface IAccessControlService
{
    AclInfo Read(string path);

    /// <summary>
    /// Gives <paramref name="principal"/> exactly <paramref name="preset"/> as an explicit entry of <paramref name="kind"/>,
    /// replacing the one it had. On a folder, <paramref name="inheritToChildren"/> makes what is created or already inside
    /// it follow: on Windows the entry is inherited by subfolders and files, on Unix it also becomes a default entry.
    /// </summary>
    void Set(string path, string principal, AclKind kind, string preset, bool inheritToChildren);

    /// <summary>Removes the explicit entry of that principal and kind; an inherited one is not touched.</summary>
    void Remove(string path, string principal, AclKind kind);

    /// <summary>Windows: whether the parent's entries flow down. Turning it off keeps them as explicit copies. Unix: not supported.</summary>
    void SetInheritance(string path, bool inherit);

    /// <summary>Changes the owner, and on Unix the group when <paramref name="group"/> is not null. A blank name is left alone.</summary>
    void SetOwner(string path, string? owner, string? group);
}
