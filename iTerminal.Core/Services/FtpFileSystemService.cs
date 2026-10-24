using FluentFTP;
using FluentFTP.Exceptions;
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

    /// <summary>The setting that lets a login with a password fall back to unencrypted FTP when the server offers no TLS.</summary>
    public const string AllowPlainSetting = "Ftp.AllowPlain";

    /// <summary>Opens a client for the host, port, user and password, encrypted (explicit FTPS) or plain; replaced by a fake in tests.</summary>
    private readonly Func<string, int, string, string, bool, IFtpClient> _clientFactory;
    private readonly Func<bool> _allowPlain;
    private readonly ILogger? _logger;
    private readonly Lock _gate = new();
    private IFtpClient? _client;
    private string _host = string.Empty;
    private string _user = string.Empty;

    public FtpFileSystemService(ILogger? logger = null, ITrustedHostStore? knownHosts = null, ISettingsService? settings = null)
        : this(CreateClientFactory(knownHosts ?? new TrustedHostStore(), logger),
            () => settings?.GetAsBool(AllowPlainSetting, false) == true,
            logger)
    {
    }

    public FtpFileSystemService(Func<string, int, string, string, bool, IFtpClient> clientFactory, Func<bool>? allowPlain = null, ILogger? logger = null)
    {
        _clientFactory = clientFactory;
        _allowPlain = allowPlain ?? (() => false);
        _logger = logger;
    }

    /// <summary>
    /// Encrypted clients use explicit FTPS (AUTH TLS) for the control and data connections. A certificate that
    /// chains to a trusted root is accepted; any other (self-signed, expired, wrong name) is trusted on first
    /// use by its SHA-256 and refused if it later changes.
    /// </summary>
    private static Func<string, int, string, string, bool, IFtpClient> CreateClientFactory(ITrustedHostStore knownHosts, ILogger? logger) =>
        (host, port, user, password, secure) =>
        {
            var client = new FtpClient(host, user, password, port);
            if (secure)
            {
                client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                client.Config.DataConnectionEncryption = true;
                client.ValidateCertificate += (_, e) =>
                {
                    if (e.PolicyErrors == System.Net.Security.SslPolicyErrors.None)
                    {
                        e.Accept = true;
                        return;
                    }

                    var fingerprint = "SHA256:" + new System.Security.Cryptography.X509Certificates.X509Certificate2(e.Certificate!)
                        .GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
                    var hostId = $"ftps:{host}:{port}";
                    var trust = knownHosts.Check(hostId, fingerprint, out var recorded);
                    e.Accept = trust != HostTrust.Changed;
                    if (trust == HostTrust.New)
                    {
                        logger?.Information("FTPS certificate of {Host} recorded: {Fingerprint} ({Errors})", hostId, fingerprint, e.PolicyErrors);
                    }
                    else if (trust == HostTrust.Changed)
                    {
                        logger?.Warning("FTPS certificate mismatch for {Host}: expected {Expected}, got {Actual}", hostId, recorded, fingerprint);
                    }
                };
            }

            return client;
        };

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

            _host = host;
            _user = user;
            _client ??= _clientFactory(name, port, user, secret, true);
            if (_client.IsConnected)
            {
                return;
            }

            try
            {
                _client.Connect();
            }
            catch (FtpSecurityNotAvailableException exception)
            {
                // The server has no TLS. Plain FTP is for logins with nothing to protect, or when the user has said so.
                if (user != AnonymousUser && !_allowPlain())
                {
                    Disconnect();
                    throw new IOException(
                        $"{name} does not offer FTPS (encrypted FTP), and a password would cross the network in clear text. " +
                        $"Set {AllowPlainSetting} to true in the settings to allow plain FTP.", exception);
                }

                _logger?.Warning("{Host} offers no FTPS; continuing with plain FTP", host);
                Disconnect();
                _client = _clientFactory(name, port, user, secret, false);
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
        try
        {
            return Listing(url);
        }
        catch (Exception exception)
        {
            // The panel shows an empty folder, and the log says why; a walk that must know uses ListChildren, which throws.
            _logger?.Error(exception, "FTP list error for {Url}", url.FullUrl);
            return [];
        }
    }

    /// <summary>Reads the folder and throws when it cannot be read, so a copy, delete or size walk never takes a failed listing for an empty folder.</summary>
    private List<FileSystemEntry> Listing(Url url)
    {
        var entries = new List<FileSystemEntry>();
        FtpListItem[] listing;
        lock (_gate)
        {
            listing = Client.GetListing(RemotePath(url));
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
        Listing(folderUrl)
            .Where(e => e.Url.DisplayName != FocusedEntryParentName && e.DisplayName != FocusedEntryParentName)
            .Select(e => e.Url)
            .ToList();

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
