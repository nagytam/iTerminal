using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Services.Mtp;

/// <summary>A device or a storage, as a row of <c>mtp://</c>. Enter opens it; it is not a file to copy or delete.</summary>
public sealed record MtpDeviceEntry : VirtualDriveEntry, IDriveObjectEntry
{
    public MtpDeviceEntry(Url url, string name, string kind)
        : base(url, name, kind)
    {
        Detail = kind;
    }
}

/// <summary>The <c>mtp://</c> drive's back-end. See <see cref="MtpDriveService"/>.</summary>
public interface IMtpDriveService : IEntryListingFileSystem
{
    /// <summary>Empty when MTP can be reached on this system; otherwise why not.</summary>
    string Unavailable { get; }

    /// <summary>Lists the devices (drive root), a device's storages, or a folder, without a <c>..</c> row.</summary>
    new Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);
}

/// <summary>
/// The <c>mtp://</c> drive: <c>mtp://&lt;device&gt;/&lt;storage&gt;/&lt;path&gt;</c>. Three levels of listing —
/// devices, a device's storages, then folders — and one file system below, all of it through an
/// <see cref="IMtpBackend"/>.
///
/// <para>MTP has no rename and no server-side copy that every device implements, so a copy within
/// a device is a download followed by an upload, a move of a file is that and a delete, and a
/// <em>folder</em> is not moved at all: it is copied and deleted by the pipelines, which walk it.
/// Nothing is ever deleted at the device or storage level — those rows are
/// <see cref="IDriveObjectEntry"/> and the service refuses them too.</para>
/// </summary>
public sealed class MtpDriveService(IMtpBackend backend) : IMtpDriveService
{
    public string Unavailable => backend.Unavailable;

    public bool IsReadOnly => false;

    #region URLs

    /// <summary>
    /// The device and the path below it, without leading or trailing slash.
    /// <c>mtp://Pixel/Internal storage/DCIM/</c> is <c>("Pixel", "Internal storage/DCIM")</c>; the
    /// drive root is <c>("", "")</c>.
    /// </summary>
    public static (string Device, string Path) Split(Url url)
    {
        var raw = url.Path.Trim('/');
        var slash = raw.IndexOf('/');
        return slash < 0 ? (raw, string.Empty) : (raw[..slash], raw[(slash + 1)..]);
    }

    private static string UrlOf(string device, string path, bool isFolder) =>
        Container.MTP + device + "/" + path + (isFolder && path.Length > 0 ? "/" : string.Empty);

    private static (string Device, string Path) RequireInsideStorage(Url url)
    {
        var (device, path) = Split(url);
        if (device.Length == 0 || path.Length == 0 || !path.Contains('/'))
        {
            throw new IOException("A device or a storage is not a file: open it to work with what is on it.");
        }

        return (device, path);
    }

    private static (string Device, string Path) RequireStorage(Url url)
    {
        var (device, path) = Split(url);
        if (device.Length == 0 || path.Length == 0)
        {
            throw new IOException("Open a device and its storage first: the root lists devices, not files.");
        }

        return (device, path);
    }

    #endregion

    #region Listing

    public async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        if (backend.Unavailable.Length > 0)
        {
            throw new NotSupportedException(backend.Unavailable);
        }

        var (device, path) = Split(url);
        if (device.Length == 0)
        {
            var devices = await backend.GetDevicesAsync(cancellationToken);
            return [.. devices
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => new MtpDeviceEntry(new Url(UrlOf(d, string.Empty, true)), d, "Device"))];
        }

        if (path.Length == 0)
        {
            var storages = await backend.GetStoragesAsync(device, cancellationToken);
            return [.. storages
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .Select(s => new MtpDeviceEntry(new Url(UrlOf(device, s, true)), s, "Storage"))];
        }

        var items = await backend.ListAsync(device, path, cancellationToken);
        return [.. items
            .Where(i => i.Name.Length > 0 && i.Name is not "." and not "..")
            .OrderBy(i => i.IsFolder ? 0 : 1)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(i => (IEntry)new VirtualDriveEntry(
                new Url(UrlOf(device, path + "/" + i.Name, i.IsFolder)),
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
            // The root and a device's list of storages are folders; whether a device is plugged in
            // is the listing's to say.
            return true;
        }

        return !path.Contains('/') || Find(url) is { IsFolder: true };
    }

    /// <summary>The row <paramref name="url"/> names, found by listing its folder; null when there is none or the device is gone.</summary>
    private MtpItem? Find(Url url)
    {
        var (device, path) = Split(url);
        var slash = path.LastIndexOf('/');
        if (device.Length == 0 || slash < 0)
        {
            return null;
        }

        try
        {
            var items = backend.ListAsync(device, path[..slash], CancellationToken.None).GetAwaiter().GetResult();
            return items.FirstOrDefault(i => string.Equals(i.Name, path[(slash + 1)..], StringComparison.Ordinal));
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
        var (device, path) = RequireInsideStorage(url);
        return backend.DownloadAsync(device, path, destination, progress, cancellationToken);
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var buffer = new MemoryStream();
        await StreamToAsync(url, buffer, progress: null, CancellationToken.None);
        return new MemoryFile(url.Name, buffer.ToArray());
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (device, path) = RequireInsideStorage(url);
        return backend.UploadAsync(device, path, source, progress, cancellationToken);
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        inputStream.Position = 0;
        return StreamFromAsync(url, inputStream, progress: null, CancellationToken.None);
    }

    public Task CreateFolderAsync(Url url)
    {
        var (device, path) = RequireInsideStorage(url);
        return backend.CreateFolderAsync(device, path.TrimEnd('/'), CancellationToken.None);
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var (device, path) = RequireInsideStorage(url);
        backend.DeleteAsync(device, path, isFolder: false, CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }

    // A phone has no recycle bin: the flag is ignored, as on the other remote drives.
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        var (device, path) = RequireInsideStorage(url);
        backend.DeleteAsync(device, path, isFolder: true, CancellationToken.None).GetAwaiter().GetResult();
        return true;
    }

    public async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (!overwrite && ExistsFile(targetUrl))
        {
            return;
        }

        using var buffer = new MemoryStream();
        await StreamToAsync(sourceUrl, buffer, null, CancellationToken.None);
        buffer.Position = 0;
        await StreamFromAsync(targetUrl, buffer, null, CancellationToken.None);
    }

    public async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (sourceUrl.PathType == PathType.Folder)
        {
            throw new NotSupportedException("A folder on a phone cannot be moved: copy it, then delete it.");
        }

        if (!overwrite && ExistsFile(targetUrl))
        {
            return;
        }

        await CopyAsync(sourceUrl, targetUrl, overwrite: true);
        DeleteFile(sourceUrl, moveToRecycleBin: false);
    }

    public string GetAvailableDiskSpace() => string.Empty;

    #endregion
}
