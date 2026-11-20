namespace iTerminal.Core.Services;

/// <summary>One entry of a listed folder: its name, and whether it is a folder.</summary>
public readonly record struct DirectoryChild(string Name, bool IsFolder);

/// <summary>
/// Lists the children of a local folder for path completion. A seam of its own, rather than a method on the
/// file-system services, so the completion can be tested without a disk and the end-to-end suite can keep it
/// inside its sandbox.
/// </summary>
public interface IDirectoryLister
{
    /// <summary>
    /// The children of <paramref name="folder"/>, at most <paramref name="max"/> of them. A folder that does not exist
    /// or cannot be read lists as empty; this never throws. It may block on a slow disk or share: call it off the UI thread.
    /// </summary>
    IReadOnlyList<DirectoryChild> List(string folder, int max);
}
