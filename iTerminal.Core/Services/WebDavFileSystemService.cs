using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml.Linq;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;

namespace iTerminal.Core.Services;

/// <summary>
/// The WebDAV drive's file system (<c>dav://[user@]host[:port]/path</c>, <c>davs://</c> for HTTPS),
/// written against plain HTTP: PROPFIND, GET, PUT, MKCOL, DELETE, MOVE and COPY through
/// <see cref="HttpClient"/> and an XML reader, so there is no package to carry for a protocol this
/// small. One client is kept for the run — both panels may be on the same server — and replaced when
/// the host, scheme or user differs.
///
/// <para>URLs hold the raw path; every segment is percent-escaped on the way to the wire and the
/// server's hrefs are unescaped on the way back, so a name with a space, a <c>#</c> or a <c>%</c>
/// round-trips. MOVE and COPY are done by the server (the <c>Destination</c> header), which is what
/// makes a rename on the drive cost nothing; a copy to another drive streams through
/// <see cref="StreamToAsync"/> / <see cref="StreamFromAsync"/>. The interface is synchronous where
/// <see cref="IFileSystem"/> is, and is only ever called from background tasks.</para>
/// </summary>
public class WebDavFileSystemService : IWebDavFileSystemService, IFolderListingFileSystem
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly HttpMethod PropFind = new("PROPFIND");
    private static readonly HttpMethod MkCol = new("MKCOL");
    private static readonly HttpMethod MoveMethod = new("MOVE");
    private static readonly HttpMethod CopyMethod = new("COPY");

    private const string PropFindBody =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop>"
        + "<d:resourcetype/><d:getcontentlength/><d:getlastmodified/></d:prop></d:propfind>";

    private readonly Func<string, string, HttpMessageHandler> _handlerFactory;
    private readonly ILogger? _logger;
    private readonly Lock _gate = new();
    private HttpClient? _client;
    private Uri? _baseAddress;
    private string _key = string.Empty;

    public WebDavFileSystemService(ILogger? logger = null)
        : this(CreateHandler, logger)
    {
    }

    /// <param name="handlerFactory">Makes the handler for a user and password; replaced by a fake in tests.</param>
    public WebDavFileSystemService(Func<string, string, HttpMessageHandler> handlerFactory, ILogger? logger = null)
    {
        _handlerFactory = handlerFactory;
        _logger = logger;
    }

    private static HttpMessageHandler CreateHandler(string user, string password)
    {
        var handler = new HttpClientHandler { PreAuthenticate = true };
        if (user.Length > 0)
        {
            handler.Credentials = new NetworkCredential(user, password);
        }

        return handler;
    }

    public bool IsReadOnly => false;

    public bool IsConnected => _client is not null;

    public string GetAvailableDiskSpace() => string.Empty;

    public void Connect(string host, bool secure, string? username, string? password)
    {
        var user = username ?? string.Empty;
        var key = $"{(secure ? "https" : "http")}://{user}@{host}";
        lock (_gate)
        {
            if (_client is not null && _key == key)
            {
                return;
            }

            Disconnect();
            _baseAddress = new Uri($"{(secure ? "https" : "http")}://{host}/");
            _client = new HttpClient(_handlerFactory(user, password ?? string.Empty), disposeHandler: true)
            {
                Timeout = TimeSpan.FromMinutes(30),
            };
            _key = key;
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            _baseAddress = null;
            _key = string.Empty;
        }
    }

    private HttpClient Client => _client ?? throw new IOException("Not connected to a WebDAV server.");

    private Uri BaseAddress => _baseAddress ?? throw new IOException("Not connected to a WebDAV server.");

    /// <summary>The server-absolute, percent-escaped path of <paramref name="path"/>.</summary>
    internal static string Escape(string path)
    {
        var trailing = path.EndsWith('/') && path.Length > 1;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return "/" + string.Join('/', segments) + (trailing ? "/" : string.Empty);
    }

    private Uri RequestUri(Url url) => new(BaseAddress, Escape(url.Path));

    private HttpResponseMessage Send(HttpRequestMessage request, HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default) =>
        Client.SendAsync(request, completion, cancellationToken).GetAwaiter().GetResult();

    private static IOException Failure(string what, HttpResponseMessage response)
    {
        var detail = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "the server wants a login (save one for this host and user)",
            HttpStatusCode.Forbidden => "the server refused it",
            HttpStatusCode.NotFound => "it is not there",
            HttpStatusCode.Conflict => "the folder it belongs in does not exist",
            HttpStatusCode.MethodNotAllowed => "the server does not allow it",
            HttpStatusCode.InsufficientStorage => "the server is out of space",
            _ => $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
        };
        return new IOException($"WebDAV {what}: {detail}.");
    }

    private static void EnsureSuccess(string what, HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(what, response);
        }
    }

    internal sealed record Resource(string Path, bool IsFolder, long Size, DateTime? Modified);

    /// <summary>PROPFIND at depth 0 or 1. Null when there is no such resource.</summary>
    private List<Resource>? PropFindAt(Url url, int depth)
    {
        using var request = new HttpRequestMessage(PropFind, RequestUri(url))
        {
            Content = new StringContent(PropFindBody, System.Text.Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", depth.ToString(CultureInfo.InvariantCulture));
        using var response = Send(request);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode && (int)response.StatusCode != 207)
        {
            throw Failure("listing", response);
        }

        var text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return ParseMultiStatus(text);
    }

    internal static List<Resource> ParseMultiStatus(string xml)
    {
        var resources = new List<Resource>();
        if (string.IsNullOrWhiteSpace(xml))
        {
            return resources;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return resources;
        }

        foreach (var response in document.Descendants(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrEmpty(href))
            {
                continue;
            }

            // Only the 200 propstat carries values; a 404 one lists the properties the server lacks.
            var prop = response.Elements(Dav + "propstat")
                .Where(p => (p.Element(Dav + "status")?.Value ?? "200").Contains(" 200", StringComparison.Ordinal))
                .Select(p => p.Element(Dav + "prop"))
                .FirstOrDefault(p => p is not null);

            var path = href.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(href, UriKind.Absolute, out var absolute)
                ? absolute.AbsolutePath
                : href;
            path = Uri.UnescapeDataString(path);

            var isFolder = prop?.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null
                || path.EndsWith('/');
            long size = 0;
            if (long.TryParse(prop?.Element(Dav + "getcontentlength")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSize))
            {
                size = parsedSize;
            }

            DateTime? modified = null;
            var lastModified = prop?.Element(Dav + "getlastmodified")?.Value;
            if (lastModified is not null
                && DateTimeOffset.TryParse(lastModified, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                modified = parsedDate.LocalDateTime;
            }

            resources.Add(new Resource(path, isFolder, size, modified));
        }

        return resources;
    }

    public IEnumerable<IEntry> List(Url url)
    {
        if (_client is null)
        {
            return [];
        }

        try
        {
            return Listing(url);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            // The panel shows an empty folder, and the log says why; a walk that must know uses ListChildren, which throws.
            _logger?.Error(exception, "WebDAV list error for {Url}", url.FullUrl);
            return [];
        }
    }

    /// <summary>Reads the folder and throws when it cannot be read, so a copy, delete or size walk never takes a failed listing for an empty folder.</summary>
    private List<FileSystemEntry> Listing(Url url)
    {
        var entries = new List<FileSystemEntry>();
        if (_client is null)
        {
            throw new IOException("Not connected to a WebDAV server.");
        }

        List<Resource>? resources;
        lock (_gate)
        {
            resources = PropFindAt(url, 1);
        }

        if (resources is null)
        {
            throw new IOException($"{url.FullUrl} could not be listed.");
        }

        var folderUrl = url.FullUrl.EndsWith('/') ? url.FullUrl : url.FullUrl + "/";
        var self = url.Path.Trim('/');
        if (self.Length > 0)
        {
            entries.Add(new FileSystemEntry("..", url.ParentUrl.FullUrl));
        }

        var rows = new List<FileSystemEntry>();
        foreach (var resource in resources)
        {
            var trimmed = resource.Path.Trim('/');
            if (trimmed == self)
            {
                continue;
            }

            var name = trimmed[(trimmed.LastIndexOf('/') + 1)..];
            if (name.Length == 0)
            {
                continue;
            }

            var childUrl = new Url(folderUrl + name + (resource.IsFolder ? "/" : string.Empty), name);
            rows.Add(new FileSystemEntry(childUrl)
            {
                FileSize = resource.IsFolder ? Url.Folder : resource.Size.ToFileSize(),
                Date = resource.Modified?.ToString("yyyy-MM-dd") ?? string.Empty,
                Time = resource.Modified?.ToString("HH:mm") ?? string.Empty,
            });
        }

        entries.AddRange(rows
            .OrderBy(r => r.Url.PathType)
            .ThenBy(r => r.Url.DisplayName, StringComparer.OrdinalIgnoreCase));
        return entries;
    }

    public IEnumerable<Url> ListChildren(Url folderUrl) =>
        Listing(folderUrl)
            .Where(e => e.Url.DisplayName != ".." && e.DisplayName != "..")
            .Select(e => e.Url)
            .ToList();

    private bool Exists(Url url, bool folder)
    {
        if (_client is null)
        {
            return false;
        }

        try
        {
            lock (_gate)
            {
                var found = PropFindAt(url, 0);
                return found is { Count: > 0 } && found[0].IsFolder == folder;
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            _logger?.Warning(exception, "WebDAV exists check failed for {Url}", url.FullUrl);
            return false;
        }
    }

    public bool ExistsFile(Url url) => Exists(url, folder: false);

    public bool ExistsFolder(Url url) => Exists(url, folder: true);

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        lock (_gate)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri(url));
            using var response = Send(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Task.FromResult<IMemoryFile?>(null);
            }

            EnsureSuccess("download", response);
            var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            return Task.FromResult<IMemoryFile?>(new MemoryFile(url, bytes));
        }
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        lock (_gate)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, RequestUri(url))
            {
                Content = new ByteArrayContent(inputStream.ToArray()),
            };
            using var response = Send(request);
            EnsureSuccess("upload", response);
        }

        return Task.CompletedTask;
    }

    public Task CreateFolderAsync(Url url)
    {
        lock (_gate)
        {
            using var request = new HttpRequestMessage(MkCol, RequestUri(url));
            using var response = Send(request);

            // 405 is "already a collection there" — creating what exists is not an error.
            if (response.StatusCode != HttpStatusCode.MethodNotAllowed)
            {
                EnsureSuccess("create folder", response);
            }
        }

        return Task.CompletedTask;
    }

    private bool Delete(Url url, bool folder)
    {
        var target = folder && !url.Path.EndsWith('/') ? new Url(url.FullUrl + "/") : url;
        lock (_gate)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, RequestUri(target));
            using var response = Send(request);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            EnsureSuccess("delete", response);
            return true;
        }
    }

    // The server has no recycle bin to offer, so the flag is ignored — as on the other remote drives.
    public bool DeleteFile(Url url, bool moveToRecycleBin) => Delete(url, folder: false);

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => Delete(url, folder: true);

    private void MoveOrCopy(HttpMethod method, Url source, Url target, bool overwrite, string what)
    {
        lock (_gate)
        {
            using var request = new HttpRequestMessage(method, RequestUri(source));
            request.Headers.Add("Destination", RequestUri(target).AbsoluteUri);
            request.Headers.Add("Overwrite", overwrite ? "T" : "F");
            using var response = Send(request);

            // 412 is "Overwrite: F and something is there" — the caller asked not to overwrite.
            if (response.StatusCode != HttpStatusCode.PreconditionFailed)
            {
                EnsureSuccess(what, response);
            }
        }
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        MoveOrCopy(MoveMethod, sourceUrl, targetUrl, overwrite, "move");
        return Task.CompletedTask;
    }

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        MoveOrCopy(CopyMethod, sourceUrl, targetUrl, overwrite, "copy");
        return Task.CompletedTask;
    }

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        HttpClient client;
        Uri uri;
        lock (_gate)
        {
            client = Client;
            uri = RequestUri(url);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        EnsureSuccess("download", response);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            progress?.Report(total);
        }
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        HttpClient client;
        Uri uri;
        lock (_gate)
        {
            client = Client;
            uri = RequestUri(url);
        }

        using var content = new StreamContent(new ProgressStream(source, progress));
        using var request = new HttpRequestMessage(HttpMethod.Put, uri) { Content = content };
        using var response = await client.SendAsync(request, cancellationToken);
        EnsureSuccess("upload", response);
    }

    /// <summary>Reports how much of the stream has been read, which for an upload is how much has been sent.</summary>
    private sealed class ProgressStream(Stream inner, IProgress<long>? progress) : Stream
    {
        private long _total;

        public override bool CanRead => true;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            Report(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            Report(read);
            return read;
        }

        private void Report(int read)
        {
            if (read > 0)
            {
                _total += read;
                progress?.Report(_total);
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose() => Disconnect();
}
