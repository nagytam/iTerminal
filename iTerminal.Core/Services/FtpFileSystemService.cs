using FluentFTP;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;

namespace iTerminal.Core.Services;

/// <summary>
/// The FTP drive's file system (<c>ftp://[user@]host[:port]/path</c>), over FluentFTP. One control
/// connection is kept and re-used; every operation takes <see cref="_gate"/> because an FTP control
/// connection is one conversation, and both panels may be on the drive at once. FTP has no
/// server-side copy, so a copy within the drive goes through memory.
/// </summary>
public class FtpFileSystemService : IFtpFileSystemService, IFolderListingFileSystem
{
    public const int DefaultPort = 21;
    public const string AnonymousUser = "anonymous";
    public const string AnonymousPassword = "anonymous@";

    /// <summary>Opens a client for the host, port, user and password; replaced by a fake in tests.</summary>
    private readonly Func<string, int, string, string, IFtpClient> _clientFactory;
    private readonly ILogger? _logger;
    private readonly Lock _gate = new();
    private IFtpClient? _client;
    private string _host = string.Empty;
    private string _user = string.Empty;

    public FtpFileSystemService(ILogger? logger = null)
        : this((host, port, user, password) => new FtpClient(host, user, password, port), logger)
    {
    }

    public FtpFileSystemService(Func<string, int, string, string, IFtpClient> clientFactory, ILogger? logger = null)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public bool IsReadOnly => false;

    public bool IsConnected => _client?.IsConnected ?? false;

    /// <summary>Splits <c>host:port</c> as <see cref="Url.Host"/> holds it.</summary>
    public static (string Host, int Port) SplitHost(string hostAndPort)
    {
        var colon = hostAndPort.LastIndexOf(':');
        if (colon > 0 && int.TryParse(hostAndPort[(colon + 1)..], out var port) && port is > 0 and <= 65535)
        {
            return (hostAndPort[..colon], port);
        }

        return (hostAndPort, DefaultPort);
    }

    public void Connect(string host, string? username, string? password)
    {
        var (name, port) = SplitHost(host);
        var user = string.IsNullOrEmpty(username) ? AnonymousUser : username;
        var secret = string.IsNullOrEmpty(username) ? AnonymousPassword : password ?? string.Empty;
        lock (_gate)
        {
            if (_client is not null && (_host != host || _user != user))
            {
                Disconnect();
            }

            _client ??= _clientFactory(name, port, user, secret);
            _host = host;
            _user = user;
            if (!_client.IsConnected)
            {
                _client.Connect();
            }
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            try
            {
                _client?.Dispose();
            }
            catch (Exception exception)
            {
                _logger?.Warning(exception, "FTP disconnect failed");
            }

            _client = null;
        }
    }

    public string GetAvailableDiskSpace() => string.Empty;

    private IFtpClient Client => _client ?? throw new IOException("Not connected to an FTP server.");

    private static string RemotePath(Url url)
    {
        var path = url.Path;
        return string.IsNullOrEmpty(path) ? "/" : path;
    }

    public IEnumerable<IEntry> List(Url url)
    {
        var entries = new List<FileSystemEntry>();
        if (_client is null)
        {
            return entries;
        }

        FtpListItem[] listing;
        try
        {
            lock (_gate)
            {
                listing = Client.GetListing(RemotePath(url));
            }
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "FTP list error for {Url}", url.FullUrl);
            return entries;
        }

        var folderUrl = url.FullUrl.EndsWith('/') ? url.FullUrl : url.FullUrl + "/";
        if (RemotePath(url).Trim('/').Length > 0)
        {
            entries.Add(new FileSystemEntry("..", url.ParentUrl.FullUrl));
        }

        var rows = new List<FileSystemEntry>();
        foreach (var item in listing)
        {
            if (item.Name is "." or ".." || string.IsNullOrEmpty(item.Name))
            {
                continue;
            }

            var isFolder = item.Type == FtpObjectType.Directory
                || (item.Type == FtpObjectType.Link && item.LinkObject?.Type == FtpObjectType.Directory);
            var childUrl = new Url(folderUrl + item.Name + (isFolder ? "/" : string.Empty), item.Name);
            var modified = item.Modified == DateTime.MinValue ? DateTime.MinValue : item.Modified;
            rows.Add(new FileSystemEntry(childUrl)
            {
                FileSize = isFolder ? Url.Folder : (item.Size < 0 ? string.Empty : item.Size.ToFileSize()),
                Date = modified == DateTime.MinValue ? string.Empty : modified.ToString("yyyy-MM-dd"),
                Time = modified == DateTime.MinValue ? string.Empty : modified.ToString("HH:mm"),
            });
        }

        entries.AddRange(rows
            .OrderBy(r => r.Url.PathType)
            .ThenBy(r => r.Url.DisplayName, StringComparer.OrdinalIgnoreCase));
        return entries;
    }

    public IEnumerable<Url> ListChildren(Url folderUrl) =>
        List(folderUrl)
            .Where(e => e.Url.DisplayName != FocusedEntryParentName && e.DisplayName != FocusedEntryParentName)
            .Select(e => e.Url);

    private const string FocusedEntryParentName = "..";

    public bool ExistsFile(Url url)
    {
        if (_client is null)
        {
            return false;
        }

        lock (_gate)
        {
            return Client.FileExists(RemotePath(url));
        }
    }

    public bool ExistsFolder(Url url)
    {
        if (_client is null)
        {
            return false;
        }

        lock (_gate)
        {
            return Client.DirectoryExists(RemotePath(url));
        }
    }

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        byte[]? bytes;
        lock (_gate)
        {
            Client.DownloadBytes(out bytes, RemotePath(url));
        }

        return Task.FromResult<IMemoryFile?>(bytes is null ? null : new MemoryFile(url, bytes));
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        lock (_gate)
        {
            inputStream.Position = 0;
            Client.UploadStream(inputStream, RemotePath(url), FtpRemoteExists.Overwrite, createRemoteDir: false);
        }

        return Task.CompletedTask;
    }

    public Task CreateFolderAsync(Url url)
    {
        lock (_gate)
        {
            Client.CreateDirectory(RemotePath(url));
        }

        return Task.CompletedTask;
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        lock (_gate)
        {
            Client.DeleteFile(RemotePath(url));
        }

        return true;
    }

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        lock (_gate)
        {
            Client.DeleteDirectory(RemotePath(url));
        }

        return true;
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var mode = overwrite ? FtpRemoteExists.Overwrite : FtpRemoteExists.Skip;
        lock (_gate)
        {
            if (sourceUrl.PathType == PathType.Folder)
            {
                Client.MoveDirectory(RemotePath(sourceUrl), RemotePath(targetUrl), mode);
            }
            else
            {
                Client.MoveFile(RemotePath(sourceUrl), RemotePath(targetUrl), mode);
            }
        }

        return Task.CompletedTask;
    }

    public async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (!overwrite && ExistsFile(targetUrl))
        {
            return;
        }

        var file = await ReadFileAsync(sourceUrl);
        using var stream = new MemoryStream(file?.Content ?? []);
        await WriteFileAsync(targetUrl, stream);
    }

    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var ftpProgress = progress is null
                    ? null
                    : (Action<FtpProgress>)(p => progress.Report(p.TransferredBytes));
                Client.DownloadStream(destination, RemotePath(url), 0, ftpProgress);
            }
        }, cancellationToken);
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var ftpProgress = progress is null
                    ? null
                    : (Action<FtpProgress>)(p => progress.Report(p.TransferredBytes));
                Client.UploadStream(source, RemotePath(url), FtpRemoteExists.Overwrite, false, ftpProgress);
            }
        }, cancellationToken);
    }

    public void Dispose() => Disconnect();
}
