using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Enumerates a folder's direct children by dispatching on <see cref="Url.ContainerType"/> — real
/// filesystem folders are enumerated via <see cref="DirectoryInfo"/>, memory-drive folders via
/// <see cref="IMemoryDriveService.GetFolder"/>. Any other drive lists through
/// <see cref="IFolderListingFileSystem"/> when its file system implements it (docker://, git://), and
/// otherwise fails clearly rather than attempting a bogus local enumeration. Shared by <see cref="FolderCopyPipeline"/> and <see cref="FolderDeletePipeline"/>.
/// </summary>
internal static class ContentModelFolderTaskHelper
{
    /// <summary>
    /// A folder link on the local disk that leads back to a folder it is inside. Walking it would go round for ever, so the
    /// walks that copy, size, compare, sync or archive a tree skip it. Other links are followed as what they point to.
    /// </summary>
    public static bool IsLinkBackToAncestor(Url url) =>
        url.PathType == PathType.Folder
        && url.ContainerType == ContainerType.FileSystem
        && url.IsLink
        && iConsole.Helper.FileLinkHelper.LeadsToAnAncestor(url.FullName);

    public static IEnumerable<Url> ListChildren(IFileSystem fileSystem, Url folderUrl)
    {
        switch (folderUrl.ContainerType)
        {
            case ContainerType.FileSystem:
                {
                    foreach (var info in new DirectoryInfo(folderUrl.FullName).EnumerateFileSystemInfos())
                    {
                        yield return new Url(info);
                    }
                    break;
                }
            case ContainerType.MemoryDrive:
                {
                    var memoryDriveService = (IMemoryDriveService)fileSystem;
                    var folder = memoryDriveService.GetFolder(folderUrl)
                        ?? throw new DirectoryNotFoundException($"Memory folder not found: {folderUrl.FullUrl}");
                    foreach (var subFolder in folder.Folders.Values)
                    {
                        yield return subFolder.Url;
                    }
                    foreach (var file in folder.Files.Values)
                    {
                        yield return file.Url;
                    }
                    break;
                }
            case ContainerType.SshFileSystem:
                {
                    var sshFileSystemService = (ISshFileSystemService)fileSystem;
                    foreach (var entry in sshFileSystemService.List(folderUrl))
                    {
                        if (entry.Url.DisplayName == "..")
                        {
                            continue;
                        }
                        yield return entry.Url;
                    }
                    break;
                }
            case ContainerType.FileSystemArchive:
                {
                    var archiveFileSystemService = (IArchiveFileSystemService)fileSystem;
                    foreach (var childUrl in archiveFileSystemService.ListChildren(folderUrl))
                    {
                        yield return childUrl;
                    }
                    break;
                }
            default:
                {
                    if (fileSystem is not IFolderListingFileSystem listing)
                    {
                        throw new NotSupportedException($"Folder copy/move is not supported for drive type '{folderUrl.ContainerType}'.");
                    }

                    foreach (var childUrl in listing.ListChildren(folderUrl))
                    {
                        yield return childUrl;
                    }
                    break;
                }
        }
    }
}
