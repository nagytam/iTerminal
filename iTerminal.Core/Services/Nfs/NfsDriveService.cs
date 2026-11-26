using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iTerminal.Core.Model.ContentModels;
#pragma warning disable S1075 // iTerminal's own URLs (not file-system paths) use '/' on every platform.

namespace iTerminal.Core.Services.Nfs;

/// <summary>An export, as a row of a host's list. Enter opens it; it is a mount, not a file, so no file operation may act on it.</summary>
public sealed record NfsExportEntry : VirtualDriveEntry, IDriveObjectEntry
{
    public NfsExportEntry(Url url, string name)
        : base(url, name, "Export")
    {
        Detail = "Export";
    }
}

/// <summary>
/// The <c>nfs://</c> drive's file system, over <see cref="INfsMountFactory"/>. Levels: the drive root lists nothing (there is
/// no host registry), a host lists its exports, an export lists its folders and files. Hosts and exports are read-only:
/// create, copy, move and delete work only inside an export.
///
/// <para>Precondition: the synchronous <see cref="IFileSystem"/> members (existence checks, delete) block on the network.
/// They are reached only from batch tasks and background work, never from a key handler. The panel's own F3, F5 and F8
/// gates answer from the listing (<see cref="ContentModelAsyncDrive"/>) and never call this service.</para>
/// </summary>
public sealed class NfsDriveService(INfsMountFactory mountFactory) : INfsDriveService, IAsyncDisposable
{
    private readonly NfsMountCache _mounts = new(mountFactory);

    public bool IsReadOnly => false;

    #region Listing

    public async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var location = NfsDriveUrl.Parse(url);
        switch (location.Depth)
        {
            case 0:
                return [];
            case 1:
                var exports = await ListExportsAsync(location.Host, cancellationToken);
                return [.. exports
                    .OrderBy(export => export, StringComparer.OrdinalIgnoreCase)
                    .Select(export => (IEntry)new NfsExportEntry(new Url(NfsDriveUrl.ExportUrl(location.Host, export)), export))];
            default:
                var items = await _mounts.RunAsync(location.Host, location.Export,
                    mount => mount.ListAsync(NfsDriveUrl.RelativePath(location.Inner), cancellationToken), cancellationToken);
                return [.. items
                    .OrderBy(item => item.IsFolder ? 0 : 1)
                    .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => (IEntry)new VirtualDriveEntry(
                        new Url(NfsDriveUrl.ChildUrl(location.Host, location.Export, location.Inner, item.Name, item.IsFolder)),
                        item.Name,
                        item.IsFolder ? Url.Folder : item.Size.ToFileSize(),
                        item.Modified)
                    {
                        Size = item.Size,
                    })];
        }
    }

    /// <summary>The host's exports; a failure names the host, as a failed mount does.</summary>
    private async Task<IReadOnlyList<string>> ListExportsAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return await mountFactory.ListExportsAsync(host, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or IOException))
        {
            throw new IOException(string.Format(UiText.Get("Nfs.ConnectionFailed", "NFS connection to {0} failed: {1}"), host, exception.Message), exception);
        }
    }

    public IEnumerable<Url> ListChildren(Url folderUrl) =>
        ListAsync(folderUrl, CancellationToken.None).GetAwaiter().GetResult().Select(entry => entry.Url);

    #endregion

    #region Existence

    public bool ExistsFile(Url url) => KindOf(url) == NfsRemoteKind.File;

    public bool ExistsFolder(Url url)
    {
        // The drive, a host and an export are folders; what is inside an export is answered by the server.
        return NfsDriveUrl.Parse(url).Depth < 3 || KindOf(url) == NfsRemoteKind.Folder;
    }

    /// <summary>
    /// What the server says is at <paramref name="url"/>, or None when the path is outside an export. A failed check throws:
    /// answering "nothing there" on a network error would let a copy or move overwrite a file that is in fact present.
    /// </summary>
    private NfsRemoteKind KindOf(Url url) => KindOfAsync(url).GetAwaiter().GetResult();

    private Task<NfsRemoteKind> KindOfAsync(Url url)
    {
        var location = NfsDriveUrl.Parse(url);
        return location.Depth < 3
            ? Task.FromResult(NfsRemoteKind.None)
            : _mounts.RunAsync(location.Host, location.Export,
                mount => mount.KindAsync(location.Inner, CancellationToken.None), CancellationToken.None);
    }

    #endregion

    #region Reading and writing

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var buffer = new MemoryStream();
        await StreamToAsync(url, buffer, progress: null, CancellationToken.None);
        return new MemoryFile(url.Name, buffer.ToArray());
    }

    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var location = RequireInside(url);
        return _mounts.ExecuteAsync(location.Host, location.Export,
            mount => mount.ReadAsync(NfsDriveUrl.RelativePath(location.Inner), new ProgressStream(destination, progress), cancellationToken), cancellationToken);
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var location = RequireInside(url);
        return _mounts.ExecuteAsync(location.Host, location.Export,
            mount => mount.WriteAsync(NfsDriveUrl.RelativePath(location.Inner), new ProgressStream(source, progress), cancellationToken), cancellationToken);
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        inputStream.Position = 0;
        return StreamFromAsync(url, inputStream, progress: null, CancellationToken.None);
    }

    /// <summary>Creates the folder unless it is already there: a tree copied into a folder that exists must not stop on the MKDIR.</summary>
    public async Task CreateFolderAsync(Url url)
    {
        var location = RequireInside(url);
        if (await KindOfAsync(url) == NfsRemoteKind.Folder)
        {
            return;
        }

        await _mounts.ExecuteAsync(location.Host, location.Export,
            mount => mount.CreateFolderAsync(NfsDriveUrl.RelativePath(location.Inner), CancellationToken.None), CancellationToken.None);
    }

    public async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        RequireInside(sourceUrl);
        RequireInside(targetUrl);
        if (sourceUrl.PathType == PathType.Folder)
        {
            // A folder's children are copied by the pipeline that walks it; here the folder itself is only created.
            await CreateFolderAsync(targetUrl);
            return;
        }

        if (!overwrite && await KindOfAsync(targetUrl) != NfsRemoteKind.None)
        {
            return;
        }

        using var buffer = new MemoryStream();
        await StreamToAsync(sourceUrl, buffer, progress: null, CancellationToken.None);
        buffer.Position = 0;
        await StreamFromAsync(targetUrl, buffer, progress: null, CancellationToken.None);
    }

    #endregion

    #region Deleting and moving

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var location = RequireInside(url);
        _mounts.ExecuteAsync(location.Host, location.Export,
            mount => mount.DeleteFileAsync(NfsDriveUrl.RelativePath(location.Inner), CancellationToken.None), CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }

    /// <summary>NFS has no recycle bin: the flag is ignored, as on the other remote drives.</summary>
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        var location = RequireInside(url);
        _mounts.ExecuteAsync(location.Host, location.Export,
            mount => mount.DeleteFolderAsync(NfsDriveUrl.RelativePath(location.Inner), recursive, CancellationToken.None), CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }

    public async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var source = RequireInside(sourceUrl);
        var target = RequireInside(targetUrl);
        if (!overwrite && await KindOfAsync(targetUrl) != NfsRemoteKind.None)
        {
            return;
        }

        if (source.Host == target.Host && source.Export == target.Export)
        {
            await _mounts.ExecuteAsync(source.Host, source.Export,
                mount => mount.MoveAsync(NfsDriveUrl.RelativePath(source.Inner), NfsDriveUrl.RelativePath(target.Inner), CancellationToken.None), CancellationToken.None);
            return;
        }

        // Between exports a file is copied and then deleted; a folder is refused, because its tree would have to be copied first.
        if (sourceUrl.PathType == PathType.Folder)
        {
            throw new IOException(UiText.Get("Nfs.MoveFolderUnsupported", "A folder cannot be moved between exports; copy it, then delete it."));
        }

        await CopyAsync(sourceUrl, targetUrl, overwrite: true);
        DeleteFile(sourceUrl, moveToRecycleBin: false);
    }

    #endregion

    public string GetAvailableDiskSpace() => string.Empty;

    /// <summary>The location of an URL that names something inside an export; refuses a host, an export and the drive root.</summary>
    private static NfsLocation RequireInside(Url url)
    {
        var location = NfsDriveUrl.Parse(url);
        if (location.Depth == 2)
        {
            throw new IOException(UiText.Get("Nfs.ExportIsNotFile", "An export is a mount, not a file: open it to work with what is inside."));
        }

        if (location.Depth < 2)
        {
            throw new IOException(UiText.Get("Nfs.ReadOnlyExport", "Hosts and exports are read-only here: create, copy, move and delete work inside an export."));
        }

        return location;
    }

    public async ValueTask DisposeAsync() => await _mounts.DisposeAsync();
}
