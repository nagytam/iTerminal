using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;

namespace iTerminal.Core.Services.Cloud;

/// <summary>The <c>onedrive://</c> drive's back-end.</summary>
public interface IOneDriveFileSystemService : IEntryListingFileSystem, IDisposable
{
    /// <summary>Selects the account (the URL's host; empty is the default one) whose token is used from now on.</summary>
    void Connect(string account);
}

/// <summary>
/// OneDrive through Microsoft Graph (<c>onedrive://[account]/path</c>), addressed by path
/// (<c>/me/drive/root:/a/b:/children</c>) so nothing has to be resolved to an id first. Small files
/// go up in one PUT, larger ones through an upload session in 10 MiB pieces (a multiple of the 320 KiB
/// Graph requires). A delete goes to OneDrive's own recycle bin, which makes it as recoverable as the
/// local drive's. A server-side copy is asynchronous: the service answers 202 with a monitor URL
/// that is polled until it completes.
/// </summary>
public class OneDriveFileSystemService : HttpFileSystemServiceBase, IOneDriveFileSystemService
{
    private const long SimpleUploadLimit = 4L * 1024 * 1024;
    private const int ChunkSize = 32 * 320 * 1024;

    private readonly ICloudTokenProvider _tokens;
    private readonly string _graph;
    private volatile string _account = "default";

    public OneDriveFileSystemService(ICloudTokenProvider tokens, ILogger? logger = null)
        : this(tokens, null, "https://graph.microsoft.com/v1.0", logger)
    {
    }

    /// <param name="handler">Replaced by a fake in tests.</param>
    /// <param name="graphBase">The API root; a test points it at its fake.</param>
    public OneDriveFileSystemService(ICloudTokenProvider tokens, HttpMessageHandler? handler, string graphBase, ILogger? logger = null)
        : base(handler, logger)
    {
        _tokens = tokens;
        _graph = graphBase.TrimEnd('/');
    }

    protected override string ServiceName => "OneDrive";

    public void Connect(string account) => _account = string.IsNullOrWhiteSpace(account) ? "default" : account;

    protected override async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Pre-authenticated URLs (an upload session, a copy monitor) carry their own access; sending
        // a bearer token there is refused by some of them.
        if (string.Equals(request.RequestUri?.Host, new Uri(_graph).Host, StringComparison.OrdinalIgnoreCase))
        {
            var token = await _tokens.GetAccessTokenAsync(_account, cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
    }

    protected override Task<bool> OnUnauthorizedAsync(CancellationToken cancellationToken)
    {
        _tokens.Invalidate(_account);
        return Task.FromResult(true);
    }

    // --- addressing ---------------------------------------------------------------------------

    private static string RawPath(Url url) => "/" + url.Path.Trim('/');

    private static string EscapePath(string rawPath) =>
        string.Join('/', rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));

    /// <summary><c>/me/drive/root</c> or <c>/me/drive/root:/a/b:</c> — what a sub-resource is appended to.</summary>
    private static string ItemAddress(string rawPath)
    {
        var escaped = EscapePath(rawPath);
        return escaped.Length == 0 ? "/me/drive/root" : "/me/drive/root:/" + escaped + ":";
    }

    private HttpRequestMessage Request(HttpMethod method, string relative, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, relative.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? relative : _graph + relative);
        if (content is not null)
        {
            request.Content = content;
        }

        return request;
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private async Task<IOException> ErrorAsync(string what, HttpResponseMessage response)
    {
        string? detail = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var c) ? c.GetString() : null;
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                detail = string.Join(": ", new[] { code, message }.Where(v => !string.IsNullOrEmpty(v)));
            }
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            // The status code alone will have to do.
        }

        return Failure(what, response, detail);
    }

    private static DateTime? ParseDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.LocalDateTime
            : null;

    // --- listing and existence ----------------------------------------------------------------

    public override async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var rows = new List<FileSystemEntry>();
        var folderUrl = UrlFolder(url);
        string? next = _graph + ItemAddress(RawPath(url)) + "/children?$top=200&$select=name,size,folder,file,lastModifiedDateTime";

        while (next is not null)
        {
            var address = next;
            using var response = await SendAsync(() => Request(HttpMethod.Get, address), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("listing", response).ConfigureAwait(false);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var isFolder = item.TryGetProperty("folder", out _);
                    var size = item.TryGetProperty("size", out var s) && s.TryGetInt64(out var parsedSize) ? parsedSize : 0;
                    rows.Add(BuildEntry(folderUrl, name, isFolder, size, ParseDate(item, "lastModifiedDateTime")));
                }
            }

            next = document.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
        }

        return Arrange(rows);
    }

    private async Task<bool?> IsFolderAsync(Url url)
    {
        if (RawPath(url) == "/")
        {
            return true;
        }

        var address = ItemAddress(RawPath(url)) + "?$select=folder,file";
        using var response = await SendAsync(() => Request(HttpMethod.Get, address)).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("lookup", response).ConfigureAwait(false);
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return document.RootElement.TryGetProperty("folder", out _);
    }

    private bool Exists(Url url, bool folder)
    {
        try
        {
            return IsFolderAsync(url).GetAwaiter().GetResult() is { } isFolder && isFolder == folder;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            Logger?.Warning(exception, "OneDrive exists check failed for {Url}", url.FullUrl);
            return false;
        }
    }

    public override bool ExistsFile(Url url) => Exists(url, folder: false);

    public override bool ExistsFolder(Url url) => Exists(url, folder: true);

    // --- transfer -----------------------------------------------------------------------------

    protected override async Task<HttpResponseMessage?> OpenDownloadAsync(Url url, CancellationToken cancellationToken)
    {
        var address = ItemAddress(RawPath(url)) + "/content";
        var response = await SendAsync(() => Request(HttpMethod.Get, address), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var failure = await ErrorAsync("download", response).ConfigureAwait(false);
            response.Dispose();
            throw failure;
        }

        return response;
    }

    private sealed class OffsetProgress(IProgress<long>? inner, long offset) : IProgress<long>
    {
        public void Report(long value) => inner?.Report(offset + value);
    }

    protected override async Task UploadAsync(Url url, Stream source, long length, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var raw = RawPath(url);
        if (raw == "/")
        {
            throw new IOException("OneDrive: a file needs a name.");
        }

        var start = source.Position;
        var item = ItemAddress(raw);
        if (length <= SimpleUploadLimit)
        {
            using var response = await SendAsync(() =>
            {
                source.Position = start;
                return Request(HttpMethod.Put, item + "/content", StreamBody(source, length, progress, "application/octet-stream"));
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("upload", response).ConfigureAwait(false);
            }

            return;
        }

        string uploadUrl;
        using (var session = await SendAsync(() => Request(HttpMethod.Post, item + "/createUploadSession",
                   Json(new Dictionary<string, object> { ["item"] = new Dictionary<string, string> { ["@microsoft.graph.conflictBehavior"] = "replace" } })),
                   cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (!session.IsSuccessStatusCode)
            {
                throw await ErrorAsync("upload", session).ConfigureAwait(false);
            }

            using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            uploadUrl = document.RootElement.GetProperty("uploadUrl").GetString()
                ?? throw new IOException("OneDrive did not start an upload session.");
        }

        long offset = 0;
        while (offset < length)
        {
            var chunk = (int)Math.Min(ChunkSize, length - offset);
            var from = offset;
            using var response = await SendAsync(() =>
            {
                source.Position = start + from;
                var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
                {
                    Content = StreamBody(source, chunk, new OffsetProgress(progress, from)),
                };
                request.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, from + chunk - 1, length);
                return request;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Accepted && !response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("upload", response).ConfigureAwait(false);
            }

            offset += chunk;
        }
    }

    // --- folders, delete, move, copy ----------------------------------------------------------

    public override async Task CreateFolderAsync(Url url)
    {
        var raw = RawPath(url);
        if (raw == "/")
        {
            return;
        }

        var name = raw[(raw.LastIndexOf('/') + 1)..];
        var parent = raw[..raw.LastIndexOf('/')];
        var parentRaw = parent.Length == 0 ? "/" : parent;
        var address = (parentRaw == "/" ? "/me/drive/root" : ItemAddress(parentRaw)) + "/children";
        using var response = await SendAsync(() => Request(HttpMethod.Post, address, Json(new Dictionary<string, object>
        {
            ["name"] = name,
            ["folder"] = new Dictionary<string, object>(),
            ["@microsoft.graph.conflictBehavior"] = "fail",
        }))).ConfigureAwait(false);

        // 409 is "already there": creating what exists is not an error.
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
        {
            throw await ErrorAsync("create folder", response).ConfigureAwait(false);
        }
    }

    private bool Delete(Url url)
    {
        var raw = RawPath(url);
        if (raw == "/")
        {
            return false;
        }

        using var response = SendAsync(() => Request(HttpMethod.Delete, ItemAddress(raw))).GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ErrorAsync("delete", response).GetAwaiter().GetResult();
        }

        return true;
    }

    // OneDrive's own recycle bin takes everything deleted through the API, so the flag has nothing to choose.
    public override bool DeleteFile(Url url, bool moveToRecycleBin) => Delete(url);

    public override bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => Delete(url);

    private static (string ParentRaw, string Name) Split(string raw)
    {
        var slash = raw.LastIndexOf('/');
        var parent = slash <= 0 ? "/" : raw[..slash];
        return (parent, raw[(slash + 1)..]);
    }

    private static string ParentReferencePath(string parentRaw) =>
        parentRaw == "/" ? "/drive/root:" : "/drive/root:" + parentRaw;

    public override async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var (parent, name) = Split(RawPath(targetUrl));
        if (!overwrite && await IsFolderAsync(targetUrl).ConfigureAwait(false) is not null)
        {
            return;
        }

        using var response = await SendAsync(() => Request(new HttpMethod("PATCH"), ItemAddress(RawPath(sourceUrl)), Json(new Dictionary<string, object>
        {
            ["parentReference"] = new Dictionary<string, string> { ["path"] = ParentReferencePath(parent) },
            ["name"] = name,
            ["@microsoft.graph.conflictBehavior"] = overwrite ? "replace" : "fail",
        }))).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("move", response).ConfigureAwait(false);
        }
    }

    public override async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var (parent, name) = Split(RawPath(targetUrl));
        var existing = await IsFolderAsync(targetUrl).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!overwrite)
            {
                return;
            }

            Delete(targetUrl);
        }

        string? monitor;
        using (var response = await SendAsync(() => Request(HttpMethod.Post, ItemAddress(RawPath(sourceUrl)) + "/copy", Json(new Dictionary<string, object>
        {
            ["parentReference"] = new Dictionary<string, string> { ["path"] = ParentReferencePath(parent) },
            ["name"] = name,
        }))).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("copy", response).ConfigureAwait(false);
            }

            monitor = response.Headers.Location?.ToString();
        }

        if (monitor is null)
        {
            return;
        }

        // The copy runs on the service; its monitor URL is public and takes no token.
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            using var poll = await Http.GetAsync(monitor).ConfigureAwait(false);
            if (poll.StatusCode == HttpStatusCode.SeeOther || poll.StatusCode == HttpStatusCode.OK && poll.Content.Headers.ContentLength == 0)
            {
                return;
            }

            if (!poll.IsSuccessStatusCode)
            {
                throw await ErrorAsync("copy", poll).ConfigureAwait(false);
            }

            using var document = JsonDocument.Parse(await poll.Content.ReadAsStringAsync().ConfigureAwait(false));
            var status = document.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("OneDrive copy: the service could not finish the copy.");
            }
        }

        throw new IOException("OneDrive copy: the service did not finish within ten minutes.");
    }
}
