using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Bluetooth;
#pragma warning disable S1075 // iTerminal's own URLs (not file-system paths) use '/' on every platform.

namespace iTerminal.Core.Services.Obex;

/// <summary>A paired device, as a row of <c>obex://</c>. Enter opens it; it is not a file to copy or delete.</summary>
public sealed record ObexDeviceEntry : VirtualDriveEntry, IDriveObjectEntry
{
    public ObexDeviceEntry(Url url, string name)
        : base(url, name, "Device")
    {
        Detail = "Device";
    }
}

/// <summary>The <c>obex://</c> drive's back-end. See <see cref="ObexDriveService"/>.</summary>
public interface IObexDriveService : IEntryListingFileSystem
{
    /// <summary>Empty when Bluetooth file transfer can be reached on this system; otherwise why not.</summary>
    string Unavailable { get; }

    /// <summary>Lists the paired devices (the drive root), or a device's folder, without a <c>..</c> row.</summary>
    new Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);
}

/// <summary>
/// The <c>obex://</c> drive: <c>obex://&lt;device address&gt;/&lt;folder&gt;/&lt;file&gt;</c>. The root lists the
/// paired Bluetooth devices from <see cref="IBluetoothService"/>; a device's root and its folders come
/// from its OBEX file service through an <see cref="IObexBackend"/>.
///
/// <para>OBEX has no rename and no server-side copy, so a copy is a download and an upload, a move of a
/// file is that and a delete, and a folder is not moved at all: the pipelines copy and delete it, walking
/// it, as they do for MTP. The root's device rows are <see cref="IDriveObjectEntry"/>s and cannot be
/// deleted.</para>
/// </summary>
public sealed class ObexDriveService(IObexBackend backend, IBluetoothService bluetooth) : IObexDriveService
{
    public string Unavailable => backend.Unavailable;

    public bool IsReadOnly => false;

    #region URLs

    /// <summary>The device address and the path below it, without leading or trailing slash. The root is <c>("", "")</c>.</summary>
    public static (string Address, string Path) Split(Url url)
    {
        var raw = url.Path.Trim('/');
        var slash = raw.IndexOf('/');
        return slash < 0 ? (raw, string.Empty) : (raw[..slash], raw[(slash + 1)..]);
    }

    private static Url UrlOf(string address, string path, bool isFolder) =>
        new(Container.OBEX + address + "/" + path + (isFolder && path.Length > 0 ? "/" : string.Empty));

    private static (string Address, string Path) RequireFile(Url url)
    {
        var (address, path) = Split(url);
        if (address.Length == 0 || path.Length == 0)
        {
            throw new IOException("A device or its root folder is not a file: open it to work with what is on it.");
        }

        return (address, path);
    }

    #endregion

    #region Listing

    public async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        if (backend.Unavailable.Length > 0)
        {
            throw new NotSupportedException(backend.Unavailable);
        }

        var (address, path) = Split(url);
        if (address.Length == 0)
        {
            var listing = await bluetooth.ListAsync(cancellationToken).ConfigureAwait(false);
            if (listing.Error is { Length: > 0 } error)
            {
                throw new IOException(error);
            }

            return [.. listing.Devices
                .Where(d => d.IsPaired)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .Select(d => (IEntry)new ObexDeviceEntry(UrlOf(d.Address, string.Empty, isFolder: true), d.Name))];
        }

        var items = await backend.ListAsync(address, path, cancellationToken).ConfigureAwait(false);
        return [.. items
            .OrderBy(i => i.IsFolder ? 0 : 1)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => (IEntry)new VirtualDriveEntry(
                UrlOf(address, path.Length == 0 ? i.Name : path + "/" + i.Name, i.IsFolder),
                i.Name,
                i.IsFolder ? Url.Folder : i.Size.ToFileSize(),
                i.Modified)
            {
                Size = i.Size,
            })];
    }

    public IEnumerable<Url> ListChildren(Url folderUrl)
    {
        foreach (var entry in ListAsync(folderUrl, CancellationToken.None).GetAwaiter().GetResult())
        {
            yield return entry.Url;
        }
    }

    public bool ExistsFile(Url url) => Find(url) is { IsFolder: false };

    public bool ExistsFolder(Url url)
    {
        var (_, path) = Split(url);
        if (path.Length == 0)
        {
            // The root and a device's root are folders; whether a device is paired is the listing's to say.
            return true;
        }

        return Find(url) is { IsFolder: true };
    }

    /// <summary>The row <paramref name="url"/> names, found by listing its folder; null when there is none or the device is gone.</summary>
    private ObexItem? Find(Url url)
    {
        var (address, path) = Split(url);
        var slash = path.LastIndexOf('/');
        var name = slash < 0 ? path : path[(slash + 1)..];
        var folder = slash < 0 ? string.Empty : path[..slash];
        if (address.Length == 0 || name.Length == 0)
        {
            return null;
        }

        try
        {
            var items = backend.ListAsync(address, folder, CancellationToken.None).GetAwaiter().GetResult();
            return items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    #endregion

    #region Reading and writing

    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (address, path) = RequireFile(url);
        return backend.DownloadAsync(address, path, destination, progress, cancellationToken);
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var buffer = new MemoryStream();
        await StreamToAsync(url, buffer, progress: null, CancellationToken.None).ConfigureAwait(false);
        return new MemoryFile(url.Name, buffer.ToArray());
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (address, path) = RequireFile(url);
        return backend.UploadAsync(address, path, source, progress, cancellationToken);
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        inputStream.Position = 0;
        return StreamFromAsync(url, inputStream, progress: null, CancellationToken.None);
    }

    public Task CreateFolderAsync(Url url)
    {
        var (address, path) = RequireFile(url);
        return backend.CreateFolderAsync(address, path.TrimEnd('/'), CancellationToken.None);
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var (address, path) = RequireFile(url);
        backend.DeleteAsync(address, path, isFolder: false, CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }

    // A phone has no recycle bin: the flag is ignored, as on the other remote drives.
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        var (address, path) = RequireFile(url);
        backend.DeleteAsync(address, path, isFolder: true, CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }

    public async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (!overwrite && ExistsFile(targetUrl))
        {
            return;
        }

        using var buffer = new MemoryStream();
        await StreamToAsync(sourceUrl, buffer, null, CancellationToken.None).ConfigureAwait(false);
        buffer.Position = 0;
        await StreamFromAsync(targetUrl, buffer, null, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (sourceUrl.PathType == PathType.Folder)
        {
            throw new NotSupportedException("A folder on a Bluetooth device cannot be moved: copy it, then delete it.");
        }

        // The same file: moving it onto itself must not end with the deletion of the only copy.
        if (string.Equals(Split(sourceUrl).Address, Split(targetUrl).Address, StringComparison.OrdinalIgnoreCase)
            && Split(sourceUrl).Path == Split(targetUrl).Path)
        {
            return;
        }

        if (!overwrite && ExistsFile(targetUrl))
        {
            return;
        }

        await CopyAsync(sourceUrl, targetUrl, overwrite: true).ConfigureAwait(false);
        DeleteFile(sourceUrl, moveToRecycleBin: false);
    }

    public string GetAvailableDiskSpace() => string.Empty;

    #endregion
}
