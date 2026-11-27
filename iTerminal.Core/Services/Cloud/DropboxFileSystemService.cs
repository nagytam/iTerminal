using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;
#pragma warning disable S1075 // iTerminal's own URLs (not file-system paths) use '/' on every platform.

namespace iTerminal.Core.Services.Cloud;

/// <summary>The <c>dropbox://</c> drive's back-end.</summary>
public interface IDropboxFileSystemService : IEntryListingFileSystem, IDisposable;

/// <summary>
/// Dropbox through its v2 API (<c>dropbox://[account]/path</c>): <c>files/list_folder</c> with its
/// continuation, <c>get_metadata</c> for existence, <c>download</c> and <c>upload</c> on the content
/// host with the arguments in the <c>Dropbox-API-Arg</c> header, and an upload session for anything
/// above 150 MiB. Dropbox has no rename, so a move is a <c>move_v2</c>, and a move or copy onto an
/// existing name first deletes that name when the caller asked to overwrite. A delete goes to
/// Dropbox's own deleted-files store, which is what a recycle bin is here.
///
/// <para>The account is a request option, not a connection: each request asks the token provider for the
/// account its URL names, so a copy between two accounts uses the right sign-in on each side.</para>
/// </summary>
public class DropboxFileSystemService : HttpFileSystemServiceBase, IDropboxFileSystemService
{
    private static readonly HttpRequestOptionsKey<string> AccountKey = new("iTerminal.Dropbox.Account");
    private const long SingleUploadLimit = 150L * 1024 * 1024;
    private const int SessionChunkSize = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions ArgumentOptions = new() { Encoder = JavaScriptEncoder.Default };

    private readonly ICloudTokenProvider _tokens;
    private readonly string _api;
    private readonly string _content;

    public DropboxFileSystemService(ICloudTokenProvider tokens, ILogger? logger = null)
        : this(tokens, null, "https://api.dropboxapi.com", "https://content.dropboxapi.com", logger)
    {
    }

    /// <param name="handler">Replaced by a fake in tests.</param>
    public DropboxFileSystemService(ICloudTokenProvider tokens, HttpMessageHandler? handler, string apiBase, string contentBase, ILogger? logger = null)
        : base(handler, logger)
    {
        _tokens = tokens;
        _api = apiBase.TrimEnd('/');
        _content = contentBase.TrimEnd('/');
    }

    protected override string ServiceName => "Dropbox";

    protected override async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var account = request.Options.TryGetValue(AccountKey, out var value) ? value : DropboxTokenProvider.DefaultAccount;
        var token = await _tokens.GetAccessTokenAsync(account, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    protected override Task<bool> OnUnauthorizedAsync(CancellationToken cancellationToken)
    {
        // The base class does not say which account the refused request was for, so every cached token
        // is dropped; the retry asks the provider again, one account at a time, as the requests come.
        _tokens.InvalidateAll();
        return Task.FromResult(true);
    }

    // --- addressing ---------------------------------------------------------------------------

    /// <summary>Dropbox's path form: the empty string is the root, otherwise <c>/a/b</c> with no trailing slash.</summary>
    internal static string DropboxPath(Url url)
    {
        var raw = url.Path.Trim('/');
        return raw.Length == 0 ? string.Empty : "/" + raw;
    }

    private static string AccountOf(Url url) => string.IsNullOrWhiteSpace(url.Host) ? DropboxTokenProvider.DefaultAccount : url.Host;

    private static HttpRequestMessage WithAccount(HttpRequestMessage request, Url url)
    {
        request.Options.Set(AccountKey, AccountOf(url));
        return request;
    }

    private static HttpRequestMessage Json(HttpMethod method, string address, object body, Url url)
    {
        var request = new HttpRequestMessage(method, address)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        return WithAccount(request, url);
    }

    private static string? ErrorSummary(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error_summary", out var summary) ? summary.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsNotFound(string? summary) => summary is not null && summary.StartsWith("path/not_found", StringComparison.Ordinal);

    /// <summary>A failed call in words: Dropbox's own error summary, with "not there" said plainly for a missing path (a 409, which the base class would call a conflict).</summary>
    private IOException Error(string what, HttpResponseMessage response, string? summary) =>
        IsNotFound(summary)
            ? new IOException($"{ServiceName} {what}: it is not there ({summary}).")
            : Failure(what, response, summary);

    private async Task<(HttpResponseMessage Response, string Body)> PostAsync(string endpoint, object body, Url url, CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(() => Json(HttpMethod.Post, _api + endpoint, body, url), cancellationToken: cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (response, text);
    }

    /// <summary>The JSON answer of an API call, or null when the path is not there and <paramref name="notFoundIsNull"/>.</summary>
    private async Task<JsonDocument?> CallAsync(string what, string endpoint, object body, Url url, bool notFoundIsNull, CancellationToken cancellationToken = default)
    {
        var (response, text) = await PostAsync(endpoint, body, url, cancellationToken).ConfigureAwait(false);
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(text);
            }

            var summary = ErrorSummary(text);
            if (notFoundIsNull && IsNotFound(summary))
            {
                return null;
            }

            throw Error(what, response, summary);
        }
    }

    private static DateTime? ParseDate(JsonElement item) =>
        item.TryGetProperty("server_modified", out var value)
        && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime()
            : null;

    // --- listing and existence ----------------------------------------------------------------

    public override async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var rows = new List<FileSystemEntry>();
        var folderUrl = UrlFolder(url);
        var page = await CallAsync("listing", "/2/files/list_folder",
            new Dictionary<string, object> { ["path"] = DropboxPath(url), ["recursive"] = false, ["limit"] = 2000 }, url, notFoundIsNull: false, cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                foreach (var item in page!.RootElement.GetProperty("entries").EnumerateArray())
                {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var isFolder = item.TryGetProperty(".tag", out var tag) && tag.GetString() == "folder";
                    var size = item.TryGetProperty("size", out var s) && s.TryGetInt64(out var parsed) ? parsed : 0;
                    rows.Add(BuildEntry(folderUrl, name, isFolder, size, ParseDate(item)));
                }

                var root = page.RootElement;
                if (!(root.TryGetProperty("has_more", out var more) && more.GetBoolean()) || !root.TryGetProperty("cursor", out var cursor))
                {
                    break;
                }

                var cursorText = cursor.GetString()!;
                var next = await CallAsync("listing", "/2/files/list_folder/continue", new Dictionary<string, object> { ["cursor"] = cursorText }, url, notFoundIsNull: false, cancellationToken).ConfigureAwait(false);
                page.Dispose();
                page = next;
            }
        }
        finally
        {
            page?.Dispose();
        }

        return Arrange(rows);
    }

    /// <summary>The kind of the item at <paramref name="url"/> — "file", "folder" — or null when there is none.</summary>
    private async Task<string?> KindAsync(Url url, CancellationToken cancellationToken = default)
    {
        if (DropboxPath(url).Length == 0)
        {
            return "folder";
        }

        using var document = await CallAsync("lookup", "/2/files/get_metadata", new Dictionary<string, object> { ["path"] = DropboxPath(url) }, url, notFoundIsNull: true, cancellationToken).ConfigureAwait(false);
        return document is null ? null : document.RootElement.GetProperty(".tag").GetString();
    }

    private async Task<bool> ExistsAsync(Url url, string kind, CancellationToken cancellationToken)
    {
        try
        {
            return await KindAsync(url, cancellationToken).ConfigureAwait(false) == kind;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Logger?.Warning(exception, "Dropbox exists check failed for {Url}", url.FullUrl);
            return false;
        }
    }

    public override Task<bool> ExistsFileAsync(Url url, CancellationToken cancellationToken) => ExistsAsync(url, "file", cancellationToken);

    public override Task<bool> ExistsFolderAsync(Url url, CancellationToken cancellationToken) => ExistsAsync(url, "folder", cancellationToken);

    // --- transfer -----------------------------------------------------------------------------

    private static string ApiArgument(object value) => JsonSerializer.Serialize(value, ArgumentOptions);

    protected override async Task<HttpResponseMessage?> OpenDownloadAsync(Url url, CancellationToken cancellationToken)
    {
        var response = await SendAsync(() =>
        {
            var request = WithAccount(new HttpRequestMessage(HttpMethod.Post, _content + "/2/files/download"), url);
            request.Headers.Add("Dropbox-API-Arg", ApiArgument(new Dictionary<string, string> { ["path"] = DropboxPath(url) }));
            return request;
        }, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var summary = ErrorSummary(text);
        response.Dispose();
        if (IsNotFound(summary))
        {
            return null;
        }

        throw new IOException($"Dropbox download: {summary ?? "the service refused the download"}.");
    }

    protected override async Task UploadAsync(Url url, Stream source, long length, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (DropboxPath(url).Length == 0)
        {
            throw new IOException("Dropbox: a file needs a name.");
        }

        var start = source.Position;
        var commit = new Dictionary<string, object> { ["path"] = DropboxPath(url), ["mode"] = "overwrite", ["autorename"] = false, ["mute"] = true };
        if (length <= SingleUploadLimit)
        {
            using var response = await SendAsync(() =>
            {
                source.Position = start;
                var request = WithAccount(new HttpRequestMessage(HttpMethod.Post, _content + "/2/files/upload") { Content = StreamBody(source, length, progress, "application/octet-stream") }, url);
                request.Headers.Add("Dropbox-API-Arg", ApiArgument(commit));
                return request;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw Error("upload", response, ErrorSummary(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            }

            return;
        }

        await UploadSessionAsync(url, source, length, start, commit, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task UploadSessionAsync(Url url, Stream source, long length, long start, Dictionary<string, object> commit, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        string sessionId;
        var firstChunk = (int)Math.Min(SessionChunkSize, length);
        using (var session = await SendAsync(() =>
               {
                   source.Position = start;
                   var request = WithAccount(new HttpRequestMessage(HttpMethod.Post, _content + "/2/files/upload_session/start") { Content = StreamBody(source, firstChunk, progress, "application/octet-stream") }, url);
                   request.Headers.Add("Dropbox-API-Arg", ApiArgument(new Dictionary<string, object> { ["close"] = false }));
                   return request;
               }, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (!session.IsSuccessStatusCode)
            {
                throw Error("upload", session, ErrorSummary(await session.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            }

            using var document = JsonDocument.Parse(await session.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            sessionId = document.RootElement.GetProperty("session_id").GetString() ?? throw new IOException("Dropbox did not start an upload session.");
        }

        long offset = firstChunk;
        while (length - offset > SessionChunkSize)
        {
            var from = offset;
            using var append = await SendAsync(() =>
            {
                source.Position = start + from;
                var request = WithAccount(new HttpRequestMessage(HttpMethod.Post, _content + "/2/files/upload_session/append_v2") { Content = StreamBody(source, SessionChunkSize, new OffsetProgress(progress, from), "application/octet-stream") }, url);
                request.Headers.Add("Dropbox-API-Arg", ApiArgument(new Dictionary<string, object> { ["cursor"] = new Dictionary<string, object> { ["session_id"] = sessionId, ["offset"] = from }, ["close"] = false }));
                return request;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!append.IsSuccessStatusCode)
            {
                throw Error("upload", append, ErrorSummary(await append.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            }

            offset += SessionChunkSize;
        }

        var last = (int)(length - offset);
        var finishAt = offset;
        using var finish = await SendAsync(() =>
        {
            source.Position = start + finishAt;
            var request = WithAccount(new HttpRequestMessage(HttpMethod.Post, _content + "/2/files/upload_session/finish") { Content = StreamBody(source, last, new OffsetProgress(progress, finishAt), "application/octet-stream") }, url);
            request.Headers.Add("Dropbox-API-Arg", ApiArgument(new Dictionary<string, object>
            {
                ["cursor"] = new Dictionary<string, object> { ["session_id"] = sessionId, ["offset"] = finishAt },
                ["commit"] = commit,
            }));
            return request;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!finish.IsSuccessStatusCode)
        {
            throw Error("upload", finish, ErrorSummary(await finish.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
        }
    }

    private sealed class OffsetProgress(IProgress<long>? inner, long offset) : IProgress<long>
    {
        public void Report(long value) => inner?.Report(offset + value);
    }

    // --- folders, delete, move, copy ----------------------------------------------------------

    public override async Task CreateFolderAsync(Url url)
    {
        if (DropboxPath(url).Length == 0)
        {
            return;
        }

        var (response, text) = await PostAsync("/2/files/create_folder_v2", new Dictionary<string, object> { ["path"] = DropboxPath(url), ["autorename"] = false }, url).ConfigureAwait(false);
        using (response)
        {
            // A conflict is "already there": creating what exists is not an error.
            var summary = ErrorSummary(text);
            if (!response.IsSuccessStatusCode && !(summary is not null && summary.StartsWith("path/conflict", StringComparison.Ordinal)))
            {
                throw Error("create folder", response, summary);
            }
        }
    }

    private async Task<bool> DeleteAsync(Url url, CancellationToken cancellationToken)
    {
        if (DropboxPath(url).Length == 0)
        {
            return false;
        }

        using var document = await CallAsync("delete", "/2/files/delete_v2", new Dictionary<string, object> { ["path"] = DropboxPath(url) }, url, notFoundIsNull: true, cancellationToken).ConfigureAwait(false);
        return document is not null;
    }

    public override Task<bool> DeleteFileAsync(Url url, bool moveToRecycleBin, CancellationToken cancellationToken) => DeleteAsync(url, cancellationToken);

    public override async Task<bool> DeleteFolderAsync(Url url, bool recursive, bool moveToRecycleBin, CancellationToken cancellationToken)
    {
        // Dropbox's delete takes the whole subtree, so a folder that is not to be deleted recursively must be empty first.
        if (!recursive && (await ListAsync(url, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            throw new IOException("Dropbox: the folder is not empty. Delete its contents first.");
        }

        return await DeleteAsync(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The source must be there before anything at the target is deleted; otherwise an overwrite would lose the target and move nothing.</summary>
    private async Task RequireSourceAsync(Url sourceUrl)
    {
        if (await KindAsync(sourceUrl).ConfigureAwait(false) is null)
        {
            throw new IOException($"Dropbox: {DropboxPath(sourceUrl)} is not there.");
        }
    }

    /// <summary>The same account and path: a move or copy onto itself changes nothing, and deleting the "target" would delete the source.</summary>
    private static bool SamePlace(Url sourceUrl, Url targetUrl) =>
        string.Equals(AccountOf(sourceUrl), AccountOf(targetUrl), StringComparison.OrdinalIgnoreCase)
        && string.Equals(DropboxPath(sourceUrl), DropboxPath(targetUrl), StringComparison.OrdinalIgnoreCase);

    public override async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (SamePlace(sourceUrl, targetUrl))
        {
            return;
        }

        await RequireSourceAsync(sourceUrl).ConfigureAwait(false);
        if (!overwrite && await KindAsync(targetUrl).ConfigureAwait(false) is not null)
        {
            return;
        }

        if (await KindAsync(targetUrl).ConfigureAwait(false) is not null)
        {
            await DeleteAsync(targetUrl, CancellationToken.None).ConfigureAwait(false);
        }

        using var document = await CallAsync("move", "/2/files/move_v2", new Dictionary<string, object>
        {
            ["from_path"] = DropboxPath(sourceUrl),
            ["to_path"] = DropboxPath(targetUrl),
            ["autorename"] = false,
            ["allow_ownership_transfer"] = false,
        }, sourceUrl, notFoundIsNull: false).ConfigureAwait(false);
    }

    public override async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        if (SamePlace(sourceUrl, targetUrl))
        {
            return;
        }

        await RequireSourceAsync(sourceUrl).ConfigureAwait(false);
        if (!overwrite && await KindAsync(targetUrl).ConfigureAwait(false) is not null)
        {
            return;
        }

        if (await KindAsync(targetUrl).ConfigureAwait(false) is not null)
        {
            await DeleteAsync(targetUrl, CancellationToken.None).ConfigureAwait(false);
        }

        using var document = await CallAsync("copy", "/2/files/copy_v2", new Dictionary<string, object>
        {
            ["from_path"] = DropboxPath(sourceUrl),
            ["to_path"] = DropboxPath(targetUrl),
            ["autorename"] = false,
            ["allow_ownership_transfer"] = false,
        }, sourceUrl, notFoundIsNull: false).ConfigureAwait(false);
    }
}
