using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;

namespace iTerminal.Core.Services.Cloud;

/// <summary>The <c>gdrive://</c> drive's back-end.</summary>
public interface IGoogleDriveFileSystemService : IEntryListingFileSystem, IDisposable
{
    /// <summary>Selects the account (the URL's host; empty is the default one) whose token is used from now on.</summary>
    void Connect(string account);
}

/// <summary>
/// Google Drive (<c>gdrive://[account]/path</c>). Drive has no paths: a file is an id with a name and
/// a list of parents, and two files may share a name. The drive presents the tree anyway by
/// resolving a path one segment at a time (<c>name = 'x' and 'parentId' in parents</c>) and caching
/// what it found for a minute, so walking a folder costs one lookup per level, not per file.
///
/// <list type="bullet">
/// <item>Where names collide the first match wins, folders before files for a segment that has more to
/// come. <c>/</c> inside a name is shown as U+2215 so it cannot be mistaken for a separator.</item>
/// <item>Delete trashes (recoverable in Drive's bin) rather than destroying.</item>
/// <item>Google-native documents have no bytes; reading one exports it (Docs to .docx, Sheets to
/// .xlsx, Slides to .pptx, drawings to PNG, anything else to PDF).</item>
/// <item>Uploads are resumable sessions in 8 MiB pieces (a multiple of the 256 KiB Drive requires).</item>
/// </list>
/// </summary>
public class GoogleDriveFileSystemService : HttpFileSystemServiceBase, IGoogleDriveFileSystemService
{
    private const string FolderMime = "application/vnd.google-apps.folder";
    private const string NativePrefix = "application/vnd.google-apps.";
    private const int ChunkSize = 32 * 256 * 1024;
    private const char SlashSubstitute = '∕';
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(60);

    private sealed record Item(string Id, string Name, string MimeType, long Size, DateTime? Modified, IReadOnlyList<string> Parents)
    {
        public bool IsFolder => MimeType == FolderMime;
    }

    private readonly ICloudTokenProvider _tokens;
    private readonly string _api;
    private readonly string _upload;
    private readonly ConcurrentDictionary<string, (Item Item, DateTime Expires)> _resolved = new(StringComparer.Ordinal);
    private volatile string _account = "default";

    public GoogleDriveFileSystemService(ICloudTokenProvider tokens, ILogger? logger = null)
        : this(tokens, null, "https://www.googleapis.com/drive/v3", "https://www.googleapis.com/upload/drive/v3", logger)
    {
    }

    /// <param name="handler">Replaced by a fake in tests.</param>
    public GoogleDriveFileSystemService(ICloudTokenProvider tokens, HttpMessageHandler? handler, string apiBase, string uploadBase, ILogger? logger = null)
        : base(handler, logger)
    {
        _tokens = tokens;
        _api = apiBase.TrimEnd('/');
        _upload = uploadBase.TrimEnd('/');
    }

    protected override string ServiceName => "Google Drive";

    public void Connect(string account)
    {
        var name = string.IsNullOrWhiteSpace(account) ? "default" : account;
        if (_account != name)
        {
            _resolved.Clear();
        }

        _account = name;
    }

    protected override async Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAccessTokenAsync(_account, cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    protected override Task<bool> OnUnauthorizedAsync(CancellationToken cancellationToken)
    {
        _tokens.Invalidate(_account);
        return Task.FromResult(true);
    }

    // --- helpers ------------------------------------------------------------------------------

    private const string Fields = "id,name,mimeType,size,modifiedTime,parents";

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static string QuoteQuery(string value) =>
        "'" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'";

    private async Task<IOException> ErrorAsync(string what, HttpResponseMessage response)
    {
        string? detail = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
            {
                detail = message.GetString();
            }
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            // The status code alone will have to do.
        }

        return Failure(what, response, detail);
    }

    private static Item ReadItem(JsonElement element)
    {
        long size = 0;
        if (element.TryGetProperty("size", out var sizeElement)
            && long.TryParse(sizeElement.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSize))
        {
            size = parsedSize;
        }

        DateTime? modified = null;
        if (element.TryGetProperty("modifiedTime", out var time)
            && DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            modified = parsed.LocalDateTime;
        }

        var parents = element.TryGetProperty("parents", out var p)
            ? p.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [];
        return new Item(
            element.GetProperty("id").GetString()!,
            element.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("mimeType", out var mime) ? mime.GetString() ?? string.Empty : string.Empty,
            size,
            modified,
            parents);
    }

    private async Task<List<Item>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var items = new List<Item>();
        string? pageToken = null;
        do
        {
            var address = $"{_api}/files?q={Uri.EscapeDataString(query)}&pageSize=1000"
                + $"&fields={Uri.EscapeDataString($"nextPageToken,files({Fields})")}"
                + "&supportsAllDrives=true&includeItemsFromAllDrives=true"
                + (pageToken is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(pageToken));
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, address), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("listing", response).ConfigureAwait(false);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("files", out var files))
            {
                items.AddRange(files.EnumerateArray().Select(ReadItem));
            }

            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (pageToken is not null);

        return items;
    }

    private static string[] Segments(Url url) =>
        url.Path.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Replace(SlashSubstitute, '/')).ToArray();

    private static string CacheKey(string[] segments) => string.Join('\u0001', segments);

    private static readonly Item RootItem = new("root", string.Empty, FolderMime, 0, null, []);

    /// <summary>The item at <paramref name="url"/>, or null when nothing has that path.</summary>
    private async Task<Item?> ResolveAsync(Url url, CancellationToken cancellationToken)
    {
        var segments = Segments(url);
        var current = RootItem;
        for (var i = 0; i < segments.Length; i++)
        {
            var key = CacheKey(segments[..(i + 1)]);
            if (_resolved.TryGetValue(key, out var cached) && cached.Expires > DateTime.UtcNow)
            {
                current = cached.Item;
                continue;
            }

            var matches = await QueryAsync(
                $"name = {QuoteQuery(segments[i])} and {QuoteQuery(current.Id)} in parents and trashed = false",
                cancellationToken).ConfigureAwait(false);
            var wantFolder = i < segments.Length - 1;
            var found = (wantFolder ? matches.FirstOrDefault(m => m.IsFolder) : null) ?? matches.FirstOrDefault();
            if (found is null)
            {
                return null;
            }

            _resolved[key] = (found, DateTime.UtcNow + CacheLifetime);
            current = found;
        }

        return current;
    }

    private void Forget(Url url) => _resolved.TryRemove(CacheKey(Segments(url)), out _);

    private static string ExportMime(string mimeType) => mimeType switch
    {
        NativePrefix + "document" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        NativePrefix + "spreadsheet" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        NativePrefix + "presentation" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        NativePrefix + "drawing" => "image/png",
        _ => "application/pdf",
    };

    // --- listing and existence ----------------------------------------------------------------

    public override async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var folder = await ResolveAsync(url, cancellationToken).ConfigureAwait(false)
            ?? throw new IOException($"Google Drive: {url.Path} is not there.");
        if (!folder.IsFolder)
        {
            throw new IOException($"Google Drive: {url.Path} is a file.");
        }

        var children = await QueryAsync($"{QuoteQuery(folder.Id)} in parents and trashed = false", cancellationToken).ConfigureAwait(false);
        var folderUrl = UrlFolder(url);
        var rows = new List<FileSystemEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in children)
        {
            var name = child.Name.Replace('/', SlashSubstitute);
            if (name.Length == 0 || !seen.Add(name + (child.IsFolder ? "/" : string.Empty)))
            {
                continue;
            }

            rows.Add(BuildEntry(folderUrl, name, child.IsFolder, child.Size, child.Modified));
        }

        return Arrange(rows);
    }

    private bool Exists(Url url, bool folder)
    {
        try
        {
            var item = ResolveAsync(url, CancellationToken.None).GetAwaiter().GetResult();
            return item is not null && item.IsFolder == folder;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            Logger?.Warning(exception, "Google Drive exists check failed for {Url}", url.FullUrl);
            return false;
        }
    }

    public override bool ExistsFile(Url url) => Exists(url, folder: false);

    public override bool ExistsFolder(Url url) => Exists(url, folder: true);

    // --- transfer -----------------------------------------------------------------------------

    protected override async Task<HttpResponseMessage?> OpenDownloadAsync(Url url, CancellationToken cancellationToken)
    {
        var item = await ResolveAsync(url, cancellationToken).ConfigureAwait(false);
        if (item is null || item.IsFolder)
        {
            return null;
        }

        var address = item.MimeType.StartsWith(NativePrefix, StringComparison.Ordinal)
            ? $"{_api}/files/{Uri.EscapeDataString(item.Id)}/export?mimeType={Uri.EscapeDataString(ExportMime(item.MimeType))}"
            : $"{_api}/files/{Uri.EscapeDataString(item.Id)}?alt=media&supportsAllDrives=true";
        var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, address), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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
        var segments = Segments(url);
        if (segments.Length == 0)
        {
            throw new IOException("Google Drive: a file needs a name.");
        }

        var start = source.Position;
        var existing = await ResolveAsync(url, cancellationToken).ConfigureAwait(false);
        string sessionAddress;
        HttpMethod sessionMethod;
        object metadata;
        if (existing is { IsFolder: false })
        {
            sessionAddress = $"{_upload}/files/{Uri.EscapeDataString(existing.Id)}?uploadType=resumable&supportsAllDrives=true";
            sessionMethod = new HttpMethod("PATCH");
            metadata = new Dictionary<string, object>();
        }
        else
        {
            var parent = await ResolveAsync(url.ParentUrl, cancellationToken).ConfigureAwait(false)
                ?? throw new IOException("Google Drive: the folder it belongs in is not there.");
            sessionAddress = $"{_upload}/files?uploadType=resumable&supportsAllDrives=true";
            sessionMethod = HttpMethod.Post;
            metadata = new Dictionary<string, object> { ["name"] = segments[^1], ["parents"] = new[] { parent.Id } };
        }

        string sessionUri;
        using (var session = await SendAsync(() =>
               {
                   var request = new HttpRequestMessage(sessionMethod, sessionAddress) { Content = Json(metadata) };
                   request.Headers.TryAddWithoutValidation("X-Upload-Content-Length", length.ToString(CultureInfo.InvariantCulture));
                   return request;
               }, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            if (!session.IsSuccessStatusCode)
            {
                throw await ErrorAsync("upload", session).ConfigureAwait(false);
            }

            sessionUri = session.Headers.Location?.ToString()
                ?? throw new IOException("Google Drive did not start an upload session.");
        }

        long offset = 0;
        do
        {
            var chunk = (int)Math.Min(ChunkSize, length - offset);
            var from = offset;
            using var response = await SendAsync(() =>
            {
                source.Position = start + from;
                var request = new HttpRequestMessage(HttpMethod.Put, sessionUri)
                {
                    Content = StreamBody(source, chunk, new OffsetProgress(progress, from)),
                };
                request.Content.Headers.ContentRange = chunk == 0
                    ? new System.Net.Http.Headers.ContentRangeHeaderValue(0)
                    : new System.Net.Http.Headers.ContentRangeHeaderValue(from, from + chunk - 1, length);
                if (chunk == 0)
                {
                    request.Content.Headers.TryAddWithoutValidation("Content-Range", "bytes */0");
                }

                return request;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);

            // 308 is "got it, send the next piece"; the final piece is answered with the file.
            if ((int)response.StatusCode != 308 && !response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("upload", response).ConfigureAwait(false);
            }

            offset += chunk;
        }
        while (offset < length);

        Forget(url);
    }

    // --- folders, delete, move, copy ----------------------------------------------------------

    public override async Task CreateFolderAsync(Url url)
    {
        var segments = Segments(url);
        if (segments.Length == 0 || await ResolveAsync(url, CancellationToken.None).ConfigureAwait(false) is { IsFolder: true })
        {
            return;
        }

        var parent = await ResolveAsync(url.ParentUrl, CancellationToken.None).ConfigureAwait(false)
            ?? throw new IOException("Google Drive: the folder it belongs in is not there.");
        var address = $"{_api}/files?fields=id&supportsAllDrives=true";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = Json(new Dictionary<string, object>
            {
                ["name"] = segments[^1],
                ["mimeType"] = FolderMime,
                ["parents"] = new[] { parent.Id },
            }),
        }).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("create folder", response).ConfigureAwait(false);
        }

        Forget(url);
    }

    private async Task<bool> TrashAsync(Url url)
    {
        var item = await ResolveAsync(url, CancellationToken.None).ConfigureAwait(false);
        if (item is null || Segments(url).Length == 0)
        {
            return false;
        }

        var address = $"{_api}/files/{Uri.EscapeDataString(item.Id)}?supportsAllDrives=true";
        using var response = await SendAsync(() => new HttpRequestMessage(new HttpMethod("PATCH"), address)
        {
            Content = Json(new Dictionary<string, object> { ["trashed"] = true }),
        }).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("delete", response).ConfigureAwait(false);
        }

        Forget(url);
        return true;
    }

    // Trashed, so recoverable from Drive's own bin; the flag has nothing to choose.
    public override bool DeleteFile(Url url, bool moveToRecycleBin) => TrashAsync(url).GetAwaiter().GetResult();

    public override bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => TrashAsync(url).GetAwaiter().GetResult();

    public override async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var item = await ResolveAsync(sourceUrl, CancellationToken.None).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Google Drive: {sourceUrl.Path} is not there.");
        var newParent = await ResolveAsync(targetUrl.ParentUrl, CancellationToken.None).ConfigureAwait(false)
            ?? throw new IOException("Google Drive: the folder it belongs in is not there.");
        var existing = await ResolveAsync(targetUrl, CancellationToken.None).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!overwrite)
            {
                return;
            }

            await TrashAsync(targetUrl).ConfigureAwait(false);
        }

        var name = Segments(targetUrl)[^1];
        var address = $"{_api}/files/{Uri.EscapeDataString(item.Id)}?supportsAllDrives=true"
            + $"&addParents={Uri.EscapeDataString(newParent.Id)}"
            + (item.Parents.Count > 0 ? $"&removeParents={Uri.EscapeDataString(string.Join(',', item.Parents))}" : string.Empty);
        using var response = await SendAsync(() => new HttpRequestMessage(new HttpMethod("PATCH"), address)
        {
            Content = Json(new Dictionary<string, object> { ["name"] = name }),
        }).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("move", response).ConfigureAwait(false);
        }

        Forget(sourceUrl);
        Forget(targetUrl);
    }

    public override async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var item = await ResolveAsync(sourceUrl, CancellationToken.None).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Google Drive: {sourceUrl.Path} is not there.");
        if (item.IsFolder)
        {
            // Drive copies single files only: a folder is made again and filled child by child.
            await CreateFolderAsync(targetUrl).ConfigureAwait(false);
            var children = await QueryAsync($"{QuoteQuery(item.Id)} in parents and trashed = false", CancellationToken.None).ConfigureAwait(false);
            var sourceBase = UrlFolder(sourceUrl);
            var targetBase = UrlFolder(targetUrl);
            foreach (var child in children)
            {
                var name = child.Name.Replace('/', SlashSubstitute);
                var suffix = child.IsFolder ? "/" : string.Empty;
                await CopyAsync(new Url(sourceBase + name + suffix), new Url(targetBase + name + suffix), overwrite).ConfigureAwait(false);
            }

            return;
        }

        var parent = await ResolveAsync(targetUrl.ParentUrl, CancellationToken.None).ConfigureAwait(false)
            ?? throw new IOException("Google Drive: the folder it belongs in is not there.");
        if (await ResolveAsync(targetUrl, CancellationToken.None).ConfigureAwait(false) is not null)
        {
            if (!overwrite)
            {
                return;
            }

            await TrashAsync(targetUrl).ConfigureAwait(false);
        }

        var address = $"{_api}/files/{Uri.EscapeDataString(item.Id)}/copy?fields=id&supportsAllDrives=true";
        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = Json(new Dictionary<string, object>
            {
                ["name"] = Segments(targetUrl)[^1],
                ["parents"] = new[] { parent.Id },
            }),
        }).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("copy", response).ConfigureAwait(false);
        }

        Forget(targetUrl);
    }
}
