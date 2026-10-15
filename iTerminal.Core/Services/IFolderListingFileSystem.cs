using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

/// <summary>
/// A file system that can list a folder's direct children. This is what a cross-drive folder copy
/// and a recursive delete walk with (<see cref="Tasks.ContentModelFolderTaskHelper"/>), so a drive
/// that implements it supports both without the helper having to learn its type.
///
/// <para>Synchronous on purpose: the walk runs on a <c>TaskManager</c> worker, never on a key
/// handler, and the helper it feeds is an iterator.</para>
/// </summary>
public interface IFolderListingFileSystem : IFileSystem
{
    /// <summary>The children of <paramref name="folderUrl"/> — never its <c>..</c> row.</summary>
    IEnumerable<Url> ListChildren(Url folderUrl);
}

/// <summary>
/// A folder-listing file system that can also say each child's size and date — the drives whose
/// listing comes from a tool or an index (archive://, docker://, git://), where the bare URLs
/// <see cref="IFolderListingFileSystem.ListChildren"/> returns carry neither. Folder comparison
/// reads through this when it can.
/// </summary>
public interface IEntryListingFileSystem : IFolderListingFileSystem
{
    /// <summary>The rows of a folder, each with its size and date where the drive knows them.</summary>
    Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);
}

/// <summary>
/// A content model whose file operations belong to a shared service. The copy, move and delete
/// commands resolve a drive's <see cref="IFileSystem"/> by URL scheme; for the drives that are only
/// reachable through their model, this is how they find it.
/// </summary>
public interface IFileSystemProvider
{
    /// <summary>The URL scheme this provider's file system serves, e.g. <c>docker://</c>.</summary>
    ContainerType ContainerType { get; }

    IFileSystem FileSystem { get; }
}
