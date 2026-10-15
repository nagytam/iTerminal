using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iMobileDevice;
using iMobileDevice.Afc;
using iMobileDevice.iDevice;
using iTerminal.Core.Model.ContentModels;
using System.Globalization;

namespace iTerminal.Core.Services;

/// <summary>
/// <see cref="IIPhoneFileSystemService"/> implemented over the Apple File Conduit (AFC) service via
/// imobiledevice-net. Paths are AFC-absolute (rooted at the device Media directory) and use '/'.
/// A single AFC session is held per service instance and reused across listings/transfers; opening a
/// session for a different UDID transparently replaces the previous one. AFC is a single-channel
/// request/response protocol, so every native call is serialized under <see cref="_lock"/>; awaited
/// stream I/O during transfers happens outside the lock so listings can interleave between chunks.
/// </summary>
public sealed class IPhoneFileSystemService : IIPhoneFileSystemService, IDisposable
{
    private const int ChunkSize = 64 * 1024;

    private readonly Lock _lock = new();
    private iDeviceHandle? _deviceHandle;
    private AfcClientHandle? _afcClient;
    private string? _connectedUdid;

    public bool IsReadOnly => false;

    public bool IsConnected
    {
        get
        {
            lock (_lock)
            {
                return _afcClient is { IsInvalid: false, IsClosed: false };
            }
        }
    }

    public void Connect(string udid)
    {
        lock (_lock)
        {
            if (_connectedUdid == udid && _afcClient is { IsInvalid: false, IsClosed: false })
            {
                return;
            }

            DisposeSession();

            var idevice = LibiMobileDevice.Instance.iDevice;
            var afc = LibiMobileDevice.Instance.Afc;

            if (idevice.idevice_new(out _deviceHandle, udid) != iDeviceError.Success || _deviceHandle is null)
            {
                throw new IOException($"Could not open iOS device {udid}. Is it connected and trusted?");
            }

            var afcError = afc.afc_client_start_service(_deviceHandle, out _afcClient, "iTerminal");
            if (afcError != AfcError.Success || _afcClient is null)
            {
                DisposeSession();
                throw new IOException($"Could not start AFC service on {udid}: {afcError}.");
            }

            _connectedUdid = udid;
        }
    }

    public IEnumerable<IEntry> List(Url url)
    {
        var entries = new List<FileSystemEntry>();
        var afc = LibiMobileDevice.Instance.Afc;
        var path = ToAfcPath(url.Path);

        lock (_lock)
        {
            if (_afcClient is null)
            {
                return entries;
            }

            if (path != "/")
            {
                entries.Add(new FileSystemEntry("..", url.ParentUrl.FullUrl));
            }

            if (afc.afc_read_directory(_afcClient, path, out var names) != AfcError.Success || names is null)
            {
                return entries;
            }

            foreach (var name in names)
            {
                if (name is "." or "..")
                {
                    continue;
                }

                var childPath = CombinePath(path, name);
                var info = GetInfo(afc, _afcClient, childPath);
                var isFolder = IsFolder(info);
                var childUrl = new Url($"{Container.IPHONE}{url.Host}{childPath}{(isFolder ? "/" : string.Empty)}");
                var modified = ParseModified(info);

                entries.Add(new FileSystemEntry(childUrl)
                {
                    FileSize = isFolder ? Url.Folder : ParseSize(info).ToFileSize(),
                    Date = modified?.ToString("yyyy-MM-dd") ?? string.Empty,
                    Time = modified?.ToString("HH:mm") ?? string.Empty,
                });
            }
        }
        return entries;
    }

    public bool ExistsFile(Url url) => ExistsAs(url, wantFolder: false);

    public bool ExistsFolder(Url url) => ExistsAs(url, wantFolder: true);

    private bool ExistsAs(Url url, bool wantFolder)
    {
        var afc = LibiMobileDevice.Instance.Afc;
        lock (_lock)
        {
            if (_afcClient is null)
            {
                return false;
            }
            var info = GetInfo(afc, _afcClient, ToAfcPath(url.Path));
            return info is not null && IsFolder(info) == wantFolder;
        }
    }

    public Task CreateFolderAsync(Url url)
    {
        var afc = LibiMobileDevice.Instance.Afc;
        lock (_lock)
        {
            if (_afcClient is not null)
            {
                afc.afc_make_directory(_afcClient, ToAfcPath(url.Path));
            }
        }
        return Task.CompletedTask;
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var afc = LibiMobileDevice.Instance.Afc;
        lock (_lock)
        {
            return _afcClient is not null
                && afc.afc_remove_path(_afcClient, ToAfcPath(url.Path)) == AfcError.Success;
        }
    }

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        var afc = LibiMobileDevice.Instance.Afc;
        var path = ToAfcPath(url.Path);
        lock (_lock)
        {
            if (_afcClient is null)
            {
                return false;
            }
            var error = recursive
                ? afc.afc_remove_path_and_contents(_afcClient, path)
                : afc.afc_remove_path(_afcClient, path);
            return error == AfcError.Success;
        }
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var afc = LibiMobileDevice.Instance.Afc;
        lock (_lock)
        {
            if (_afcClient is null)
            {
                return Task.CompletedTask;
            }
            var error = afc.afc_rename_path(_afcClient, ToAfcPath(sourceUrl.Path), ToAfcPath(targetUrl.Path));
            if (error != AfcError.Success)
            {
                throw new IOException($"AFC move failed: {error}.");
            }
        }
        return Task.CompletedTask;
    }

    public async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (sourceUrl.PathType == PathType.Folder)
        {
            await CreateFolderAsync(targetUrl);
            foreach (var entry in List(sourceUrl))
            {
                if (entry.DisplayName == "..")
                {
                    continue;
                }
                var isChildFolder = entry.Url.PathType == PathType.Folder;
                var childTargetUrl = new Url(targetUrl.FullUrl.TrimEnd('/') + "/" + entry.Name + (isChildFolder ? "/" : string.Empty));
                await CopyAsync(entry.Url, childTargetUrl, overwrite);
            }
        }
        else
        {
            var file = await ReadFileAsync(sourceUrl);
            if (file?.Content is not null)
            {
                using var stream = new MemoryStream(file.Content);
                await WriteFileAsync(targetUrl, stream);
            }
        }
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var memoryStream = new MemoryStream();
        await StreamToAsync(url, memoryStream, progress: null, CancellationToken.None);
        return new MemoryFile(url, memoryStream.ToArray());
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        inputStream.Position = 0;
        return StreamFromAsync(url, inputStream, progress: null, CancellationToken.None);
    }

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var afc = LibiMobileDevice.Instance.Afc;
        var path = ToAfcPath(url.Path);
        ulong handle;
        lock (_lock)
        {
            if (_afcClient is null)
            {
                throw new IOException("Not connected to an iOS device.");
            }
            handle = 0;
            var openError = afc.afc_file_open(_afcClient, path, AfcFileMode.FopenRdonly, ref handle);
            if (openError != AfcError.Success)
            {
                throw new IOException($"AFC open for read failed: {openError}.");
            }
        }

        try
        {
            var buffer = new byte[ChunkSize];
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                uint bytesRead;
                lock (_lock)
                {
                    if (_afcClient is null)
                    {
                        throw new IOException("iOS device disconnected during read.");
                    }
                    bytesRead = 0;
                    var readError = afc.afc_file_read(_afcClient, handle, buffer, (uint)buffer.Length, ref bytesRead);
                    if (readError != AfcError.Success)
                    {
                        throw new IOException($"AFC read failed: {readError}.");
                    }
                }
                if (bytesRead == 0)
                {
                    break;
                }
                await destination.WriteAsync(buffer.AsMemory(0, (int)bytesRead), cancellationToken);
                total += bytesRead;
                progress?.Report(total);
            }
        }
        finally
        {
            CloseFile(afc, handle);
        }
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var afc = LibiMobileDevice.Instance.Afc;
        var path = ToAfcPath(url.Path);
        ulong handle;
        lock (_lock)
        {
            if (_afcClient is null)
            {
                throw new IOException("Not connected to an iOS device.");
            }
            handle = 0;
            var openError = afc.afc_file_open(_afcClient, path, AfcFileMode.FopenWronly, ref handle);
            if (openError != AfcError.Success)
            {
                throw new IOException($"AFC open for write failed: {openError}.");
            }
        }

        try
        {
            var buffer = new byte[ChunkSize];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                lock (_lock)
                {
                    if (_afcClient is null)
                    {
                        throw new IOException("iOS device disconnected during write.");
                    }
                    uint bytesWritten = 0;
                    var chunk = read == buffer.Length ? buffer : buffer[..read];
                    var writeError = afc.afc_file_write(_afcClient, handle, chunk, (uint)read, ref bytesWritten);
                    if (writeError != AfcError.Success)
                    {
                        throw new IOException($"AFC write failed: {writeError}.");
                    }
                }
                total += read;
                progress?.Report(total);
            }
        }
        finally
        {
            CloseFile(afc, handle);
        }
    }

    public string GetAvailableDiskSpace()
    {
        var afc = LibiMobileDevice.Instance.Afc;
        lock (_lock)
        {
            if (_afcClient is null)
            {
                return string.Empty;
            }
            if (afc.afc_get_device_info_key(_afcClient, "FSFreeBytes", out var value) == AfcError.Success
                && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var freeBytes))
            {
                return freeBytes.ToFileSize();
            }
            return string.Empty;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            DisposeSession();
        }
    }

    private void CloseFile(IAfcApi afc, ulong handle)
    {
        lock (_lock)
        {
            if (_afcClient is not null)
            {
                try { afc.afc_file_close(_afcClient, handle); } catch { }
            }
        }
    }

    private void DisposeSession()
    {
        try { _afcClient?.Dispose(); } catch { }
        try { _deviceHandle?.Dispose(); } catch { }
        _afcClient = null;
        _deviceHandle = null;
        _connectedUdid = null;
    }

    private static IReadOnlyDictionary<string, string>? GetInfo(IAfcApi afc, AfcClientHandle client, string path)
    {
        if (afc.afc_get_file_info(client, path, out var pairs) != AfcError.Success || pairs is null)
        {
            return null;
        }
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < pairs.Count; i += 2)
        {
            dict[pairs[i]] = pairs[i + 1];
        }
        return dict;
    }

    private static bool IsFolder(IReadOnlyDictionary<string, string>? info) =>
        info is not null && info.TryGetValue("st_ifmt", out var fmt) && fmt == "S_IFDIR";

    private static long ParseSize(IReadOnlyDictionary<string, string>? info) =>
        info is not null && info.TryGetValue("st_size", out var size)
            && long.TryParse(size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static DateTime? ParseModified(IReadOnlyDictionary<string, string>? info)
    {
        if (info is not null && info.TryGetValue("st_mtime", out var mtime)
            && long.TryParse(mtime, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nanoseconds))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(nanoseconds / 1_000_000).LocalDateTime;
        }
        return null;
    }

    private static string ToAfcPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }
        var trimmed = path.TrimEnd('/');
        return trimmed.Length == 0 ? "/" : trimmed;
    }

    private static string CombinePath(string parent, string name)
    {
        var basePath = parent == "/" ? string.Empty : parent.TrimEnd('/');
        return $"{basePath}/{name}";
    }
}
