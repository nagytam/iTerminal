using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Services;

/// <summary>
/// The <see cref="IFileSystem"/> that serves a URL: the shared service for the local disk, the memory
/// drive, SSH and <c>zip://</c>; for the drives only reachable through their panel (<c>archive://</c>,
/// <c>docker://</c>, <c>git://</c>), the file system that panel hands over; and, last, the panel's own
/// model, which is an <see cref="IFileSystem"/> too.
///
/// <para>The one place that answers this. Copy, move, delete, compress, extract and compare used to
/// carry a switch each, and a drive added to one of them was still missing from the others.</para>
/// </summary>
public sealed class FileSystemResolver(
    IFileSystemService fileSystemService,
    IMemoryDriveService memoryDriveService,
    ISshFileSystemService sshFileSystemService,
    IArchiveFileSystemService archiveFileSystemService,
    IContentModelService contentModelService)
{
    /// <param name="model">The panel showing <paramref name="url"/>, when there is one — the fallback for a drive with no shared service.</param>
    /// <param name="operation">What the caller is doing ("Copy"), for the message when nothing serves the URL.</param>
    public IFileSystem Resolve(Url url, IContentModel? model = null, string? operation = null) => url.ContainerType switch
    {
        ContainerType.FileSystem => fileSystemService,
        ContainerType.MemoryDrive => memoryDriveService,
        ContainerType.SshFileSystem => sshFileSystemService,
        ContainerType.FileSystemArchive => archiveFileSystemService,
        _ => (model as IFileSystemProvider)?.FileSystem
             ?? contentModelService.ContentModels
                 .OfType<IFileSystemProvider>()
                 .FirstOrDefault(provider => provider.ContainerType == url.ContainerType)
                 ?.FileSystem
             ?? PanelServing(url)
             ?? model
             ?? throw new NotSupportedException($"{operation ?? "This"} is not supported on {url.ContainerType} drives."),
    };

    /// <summary>
    /// The panel that is showing the same remote account as <paramref name="url"/> — FTP, SMB, WebDAV and
    /// the iPhone have a connection per panel (the SMB service is even transient), so the panel's own model,
    /// which delegates to that connection, is the one file system that can reach it. Matched on the host
    /// (and the share): handing a URL to a panel on another server would write to the wrong machine.
    /// </summary>
    private IFileSystem? PanelServing(Url url) => url.ContainerType is ContainerType.FtpFileSystem
        or ContainerType.SmbFileSystem
        or ContainerType.WebDav
        or ContainerType.IPhoneFileSystem
            ? contentModelService.ContentModels.FirstOrDefault(panel =>
                panel.Url is { } shown
                && shown.ContainerType == url.ContainerType
                && string.Equals(shown.Host, url.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(shown.Share, url.Share, StringComparison.OrdinalIgnoreCase))
            : null;
}
