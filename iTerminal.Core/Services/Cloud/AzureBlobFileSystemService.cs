using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;
#pragma warning disable S1075 // iTerminal's own URLs (not file-system paths) use '/' on every platform.

namespace iTerminal.Core.Services.Cloud;

/// <summary>The <c>azblob://</c> drive's back-end.</summary>
public interface IAzureBlobFileSystemService : IEntryListingFileSystem, IDisposable;

/// <summary>
/// Azure Blob Storage (<c>azblob://account/container/path</c>) over the REST API with
/// <see cref="HttpFileSystemServiceBase"/>: <c>List Blobs</c> with a <c>/</c> delimiter is what makes
/// the flat blob names look like folders, a zero-byte blob whose name ends in <c>/</c> is how an empty
/// folder is kept, and a move is a server-side <c>Copy Blob</c> followed by a delete, because Blob
/// Storage has no rename. A copy between two accounts cannot be server-side without a SAS, so it
/// streams through a temporary file. Requests are signed with the account key (SharedKey) when the
/// credentials have one, and sent anonymously when they do not.
///
/// <para>The account is a request option, so a panel and a copy can name different accounts. A block
/// blob is written in one <c>Put Blob</c> up to 256 MiB and in blocks above that.</para>
/// </summary>
public class AzureBlobFileSystemService : HttpFileSystemServiceBase, IAzureBlobFileSystemService
{
    private const string Version = "2023-11-03";
    private static readonly HttpRequestOptionsKey<string> AccountKey = new("iTerminal.AzureBlob.Account");
    private static readonly HttpMethod Head = HttpMethod.Head;
    private static readonly HttpMethod Put = HttpMethod.Put;
    private const long SingleUploadLimit = 256L * 1024 * 1024;
    private const int BlockSize = 8 * 1024 * 1024;
    private static readonly TimeSpan CopyDeadline = TimeSpan.FromMinutes(10);

    private readonly Func<string, AzureBlobCredentials> _credentialsFor;

    public AzureBlobFileSystemService(Func<string, AzureBlobCredentials> credentialsFor, ILogger? logger = null)
        : this(credentialsFor, null, logger)
    {
    }

    /// <param name="handler">Replaced by a fake in tests.</param>
    public AzureBlobFileSystemService(Func<string, AzureBlobCredentials> credentialsFor, HttpMessageHandler? handler, ILogger? logger = null)
        : base(handler, logger)
    {
        _credentialsFor = credentialsFor;
    }

    protected override string ServiceName => "Azure Blob Storage";

    // --- addressing ---------------------------------------------------------------------------

    private static string AccountOf(Url url) => url.Host;

    /// <summary>The first path segment: the container. Empty at the account's own root.</summary>
    internal static string ContainerOf(Url url)
    {
        var raw = url.Path.Trim('/');
        var slash = raw.IndexOf('/');
        return slash < 0 ? raw : raw[..slash];
    }

    /// <summary>The blob's name inside its container, without a trailing slash; empty for the container itself.</summary>
    internal static string BlobNameOf(Url url)
    {
        var raw = url.Path.Trim('/');
        var slash = raw.IndexOf('/');
        return slash < 0 ? string.Empty : raw[(slash + 1)..].TrimEnd('/');
    }

    /// <summary>Each segment escaped, the separators kept; a trailing slash (a folder's marker) is kept too.</summary>
    private static string EscapeName(string name)
    {
        var escaped = string.Join('/', name.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        return name.EndsWith('/') && escaped.Length > 0 ? escaped + "/" : escaped;
    }

    private string BaseFor(string account)
    {
        var endpoint = _credentialsFor(account).Endpoint;
        return string.IsNullOrWhiteSpace(endpoint)
            ? $"https://{account}.blob.core.windows.net"
            : endpoint.TrimEnd('/') + "/" + account;
    }

    /// <summary>A request to <paramref name="pathAndQuery"/> of <paramref name="account"/>.</summary>
    private HttpRequestMessage Request(HttpMethod method, string account, string pathAndQuery, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, BaseFor(account) + pathAndQuery);
        if (content is not null)
        {
            request.Content = content;
        }

        request.Options.Set(AccountKey, account);
        return request;
    }

    private static string BlobPath(string container, string blobName) =>
        "/" + Uri.EscapeDataString(container) + (blobName.Length == 0 ? string.Empty : "/" + EscapeName(blobName));

    private static string Query(params (string Name, string? Value)[] parameters) =>
        "?" + string.Join('&', parameters.Where(p => p.Value is not null).Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));

    // --- signing ------------------------------------------------------------------------------

    protected override Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var account = request.Options.TryGetValue(AccountKey, out var value) ? value : string.Empty;
        request.Headers.TryAddWithoutValidation("x-ms-version", Version);
        request.Headers.TryAddWithoutValidation("x-ms-date", DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture));
        var key = _credentialsFor(account).Key;
        if (key is not null)
        {
            var signature = Sign(request, account, key);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("SharedKey", $"{account}:{signature}");
        }

        return Task.CompletedTask;
    }

    /// <summary>The SharedKey signature: HMAC-SHA256 of the string-to-sign, keyed with the base64-decoded account key.</summary>
    internal static string Sign(HttpRequestMessage request, string account, string base64Key)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(base64Key));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(StringToSign(request, account))));
    }

    /// <summary>
    /// The string that is signed: the verb, eleven standard headers (empty when absent, and an empty
    /// Content-Length for zero), the <c>x-ms-</c> headers sorted and lower-cased, then the canonical
    /// resource — the account and the request path, then each query parameter lower-cased and sorted.
    /// </summary>
    internal static string StringToSign(HttpRequestMessage request, string account)
    {
        var headers = request.Headers;
        var content = request.Content?.Headers;

        static string Header(HttpHeaders source, string name) =>
            source.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

        var length = content?.ContentLength is { } l && l > 0 ? l.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var standard = new[]
        {
            request.Method.Method,
            content is null ? string.Empty : string.Join(",", content.ContentEncoding),
            content is null ? string.Empty : string.Join(",", content.ContentLanguage),
            length,
            string.Empty,
            content?.ContentType?.ToString() ?? string.Empty,
            string.Empty,
            Header(headers, "If-Modified-Since"),
            Header(headers, "If-Match"),
            Header(headers, "If-None-Match"),
            Header(headers, "If-Unmodified-Since"),
            string.Empty,
        };

        var msHeaders = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in new HttpHeaders?[] { headers, content })
        {
            if (source is null)
            {
                continue;
            }

            foreach (var header in source)
            {
                var name = header.Key.ToLowerInvariant();
                if (name.StartsWith("x-ms-", StringComparison.Ordinal))
                {
                    msHeaders[name] = string.Join(",", header.Value).Trim();
                }
            }
        }

        var canonicalHeaders = new StringBuilder();
        foreach (var (name, value) in msHeaders)
        {
            canonicalHeaders.Append(name).Append(':').Append(value).Append('\n');
        }

        var uri = request.RequestUri!;
        var canonicalResource = new StringBuilder("/").Append(account).Append(Uri.UnescapeDataString(uri.AbsolutePath));
        var query = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).ToLowerInvariant());
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..]);
            if (!query.TryGetValue(name, out var values))
            {
                query[name] = values = [];
            }

            values.Add(value);
        }

        foreach (var (name, values) in query)
        {
            values.Sort(StringComparer.Ordinal);
            canonicalResource.Append('\n').Append(name).Append(':').Append(string.Join(",", values));
        }

        return string.Join('\n', standard) + "\n" + canonicalHeaders + canonicalResource;
    }

    // --- errors -------------------------------------------------------------------------------

    private static string? ErrorCode(string body)
    {
        try
        {
            return XDocument.Parse(body).Root?.Element("Code")?.Value;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    // --- listing and existence ----------------------------------------------------------------

    public override async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var account = AccountOf(url);
        var folderUrl = UrlFolder(url);
        var rows = new List<FileSystemEntry>();
        var container = ContainerOf(url);
        var prefix = BlobNameOf(url);
        prefix = prefix.Length == 0 ? string.Empty : prefix + "/";
        string? marker = null;

        do
        {
            var next = marker;
            var listPrefix = prefix.Length == 0 ? null : prefix;
            var address = container.Length == 0
                ? "/" + Query(("comp", "list"), ("marker", next))
                : "/" + Uri.EscapeDataString(container) + Query(("restype", "container"), ("comp", "list"), ("delimiter", "/"), ("prefix", listPrefix), ("marker", next));
            var document = await CallAsync("listing", account, address, cancellationToken).ConfigureAwait(false);
            var root = document.Root!;
            if (container.Length == 0)
            {
                foreach (var item in root.Element("Containers")?.Elements("Container") ?? [])
                {
                    var name = item.Element("Name")?.Value;
                    if (!string.IsNullOrEmpty(name))
                    {
                        rows.Add(BuildEntry(folderUrl, name, isFolder: true, 0, ParseDate(item)));
                    }
                }
            }
            else
            {
                foreach (var prefixElement in root.Element("Blobs")?.Elements("BlobPrefix") ?? [])
                {
                    var full = prefixElement.Element("Name")?.Value ?? string.Empty;
                    var name = full.StartsWith(prefix, StringComparison.Ordinal) ? full[prefix.Length..].TrimEnd('/') : full.TrimEnd('/');
                    if (name.Length > 0)
                    {
                        rows.Add(BuildEntry(folderUrl, name, isFolder: true, 0, null));
                    }
                }

                foreach (var blob in root.Element("Blobs")?.Elements("Blob") ?? [])
                {
                    var full = blob.Element("Name")?.Value ?? string.Empty;
                    var name = full.StartsWith(prefix, StringComparison.Ordinal) ? full[prefix.Length..] : full;
                    if (name.Length == 0 || name.EndsWith('/'))
                    {
                        // The zero-byte marker of this very folder, or a nested marker the delimiter did not fold.
                        continue;
                    }

                    var properties = blob.Element("Properties");
                    var size = long.TryParse(properties?.Element("Content-Length")?.Value, out var parsed) ? parsed : 0;
                    rows.Add(BuildEntry(folderUrl, name, isFolder: false, size, ParseDate(blob)));
                }
            }

            marker = root.Element("NextMarker")?.Value is { Length: > 0 } token ? token : null;
        }
        while (marker is not null);

        return Arrange(rows);
    }

    private static DateTime? ParseDate(XElement item) =>
        DateTime.TryParse(item.Element("Properties")?.Element("Last-Modified")?.Value ?? item.Element("Last-Modified")?.Value,
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime()
            : null;

    private async Task<XDocument> CallAsync(string what, string account, string address, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => Request(HttpMethod.Get, account, address), cancellationToken: cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw Failure(what, response, ErrorCode(body));
        }

        return XDocument.Parse(body);
    }

    private async Task<HttpResponseMessage> HeadAsync(string account, string address, CancellationToken cancellationToken = default)
    {
        return await SendAsync(() => Request(Head, account, address), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether a blob (not a folder) is there: a HEAD answers 200 for it.</summary>
    private async Task<bool> BlobExistsAsync(Url url, CancellationToken cancellationToken)
    {
        var name = BlobNameOf(url);
        if (ContainerOf(url).Length == 0 || name.Length == 0)
        {
            return false;
        }

        try
        {
            using var response = await HeadAsync(AccountOf(url), BlobPath(ContainerOf(url), name), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Logger?.Warning(exception, "Azure Blob exists check failed for {Url}", url.FullUrl);
            return false;
        }
    }

    /// <summary>A folder is there when its container is, or when anything is listed under its prefix.</summary>
    private async Task<bool> FolderExistsAsync(Url url, CancellationToken cancellationToken)
    {
        var container = ContainerOf(url);
        if (container.Length == 0)
        {
            return true;
        }

        var name = BlobNameOf(url);
        var prefix = name.Length == 0 ? null : name + "/";
        try
        {
            var address = "/" + Uri.EscapeDataString(container) + Query(("restype", "container"), ("comp", "list"), ("prefix", prefix), ("maxresults", "1"));
            using var response = await SendAsync(() => Request(HttpMethod.Get, AccountOf(url), address), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var document = XDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var blobs = document.Root?.Element("Blobs");
            return name.Length == 0 || (blobs is not null && (blobs.Elements().Any()));
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or System.Xml.XmlException && !cancellationToken.IsCancellationRequested)
        {
            Logger?.Warning(exception, "Azure Blob folder check failed for {Url}", url.FullUrl);
            return false;
        }
    }

    public override Task<bool> ExistsFileAsync(Url url, CancellationToken cancellationToken) => BlobExistsAsync(url, cancellationToken);

    public override Task<bool> ExistsFolderAsync(Url url, CancellationToken cancellationToken) => FolderExistsAsync(url, cancellationToken);

    // --- transfer -----------------------------------------------------------------------------

    protected override async Task<HttpResponseMessage?> OpenDownloadAsync(Url url, CancellationToken cancellationToken)
    {
        var account = AccountOf(url);
        var response = await SendAsync(() => Request(HttpMethod.Get, account, BlobPath(ContainerOf(url), BlobNameOf(url))),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var code = ErrorCode(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var failure = Failure("download", response, code);
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
        var account = AccountOf(url);
        var container = ContainerOf(url);
        var name = BlobNameOf(url);
        if (container.Length == 0 || name.Length == 0)
        {
            throw new IOException("Azure Blob Storage: a file needs a container and a name.");
        }

        var start = source.Position;
        var path = BlobPath(container, name);
        if (length <= SingleUploadLimit)
        {
            using var response = await SendAsync(() =>
            {
                source.Position = start;
                var request = Request(Put, account, path, StreamBody(source, length, progress, "application/octet-stream"));
                request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
                return request;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw Failure("upload", response, ErrorCode(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            }

            return;
        }

        var blockIds = new List<string>();
        long offset = 0;
        while (offset < length)
        {
            var chunk = (int)Math.Min(BlockSize, length - offset);
            var id = Convert.ToBase64String(Encoding.UTF8.GetBytes(blockIds.Count.ToString("D6", CultureInfo.InvariantCulture)));
            var from = offset;
            using var block = await SendAsync(() =>
            {
                source.Position = start + from;
                return Request(Put, account, path + Query(("comp", "block"), ("blockid", id)), StreamBody(source, chunk, new OffsetProgress(progress, from), "application/octet-stream"));
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!block.IsSuccessStatusCode)
            {
                throw Failure("upload", block, ErrorCode(await block.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
            }

            blockIds.Add(id);
            offset += chunk;
        }

        var list = new XDocument(new XElement("BlockList", blockIds.Select(id => new XElement("Latest", id))));
        var body = list.ToString(SaveOptions.DisableFormatting);
        using var commit = await SendAsync(() => Request(Put, account, path + Query(("comp", "blocklist")),
            new StringContent(body, Encoding.UTF8, "application/xml")), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!commit.IsSuccessStatusCode)
        {
            throw Failure("upload", commit, ErrorCode(await commit.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
        }
    }

    // --- folders, delete, move, copy ----------------------------------------------------------

    public override async Task CreateFolderAsync(Url url)
    {
        var account = AccountOf(url);
        var container = ContainerOf(url);
        var name = BlobNameOf(url);
        if (container.Length == 0)
        {
            return;
        }

        if (name.Length == 0)
        {
            // A container is created at the top of an account, not inside one.
            using var created = await SendAsync(() => Request(Put, account, "/" + Uri.EscapeDataString(container) + Query(("restype", "container")))).ConfigureAwait(false);
            if (!created.IsSuccessStatusCode && created.StatusCode != HttpStatusCode.Conflict)
            {
                throw Failure("create container", created, ErrorCode(await created.Content.ReadAsStringAsync().ConfigureAwait(false)));
            }

            return;
        }

        // The marker of an empty folder: a zero-byte blob named "folder/". It is not overwritten when it is there.
        using var response = await SendAsync(() =>
        {
            var request = Request(Put, account, BlobPath(container, name + "/"), new ByteArrayContent([]));
            request.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
            request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            return request;
        }).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PreconditionFailed)
        {
            throw Failure("create folder", response, ErrorCode(await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
        }
    }

    private async Task<bool> DeleteBlobAsync(string account, string container, string blobName, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => Request(HttpMethod.Delete, account, BlobPath(container, blobName)), cancellationToken: cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw Failure("delete", response, ErrorCode(await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
        }

        return true;
    }

    private async Task<bool> DeleteAsync(Url url, bool recursive, bool isFolder, CancellationToken cancellationToken)
    {
        var account = AccountOf(url);
        var container = ContainerOf(url);
        var name = BlobNameOf(url);
        if (container.Length == 0)
        {
            return false;
        }

        if (name.Length == 0)
        {
            // Deleting a container takes its blobs with it, so only the recursive delete does that.
            if (!recursive && (await ListFlatAsync(account, container, string.Empty, cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                throw new IOException("Azure Blob Storage: the container is not empty. Delete its contents first, or delete the container recursively.");
            }

            using var response = await SendAsync(() => Request(HttpMethod.Delete, account, "/" + Uri.EscapeDataString(container) + Query(("restype", "container"))), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Failure("delete", response, ErrorCode(await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
            }

            return true;
        }

        if (!recursive)
        {
            // A folder's own entry is its marker, "name/"; a file is the blob itself.
            return await DeleteBlobAsync(account, container, isFolder ? name + "/" : name, cancellationToken).ConfigureAwait(false);
        }

        var removed = false;
        foreach (var blob in await ListFlatAsync(account, container, name + "/", cancellationToken).ConfigureAwait(false))
        {
            removed = await DeleteBlobAsync(account, container, blob, cancellationToken).ConfigureAwait(false) || removed;
        }

        // The marker is deleted too, whatever the children did: both results are needed, so neither call is skipped.
        var markerRemoved = await DeleteBlobAsync(account, container, name + "/", cancellationToken).ConfigureAwait(false);
        return removed || markerRemoved;
    }

    public override Task<bool> DeleteFileAsync(Url url, bool moveToRecycleBin, CancellationToken cancellationToken) => DeleteAsync(url, recursive: false, isFolder: false, cancellationToken);

    public override Task<bool> DeleteFolderAsync(Url url, bool recursive, bool moveToRecycleBin, CancellationToken cancellationToken) => DeleteAsync(url, recursive, isFolder: true, cancellationToken);

    /// <summary>Every blob name under <paramref name="prefix"/>, markers included, with no delimiter.</summary>
    private async Task<List<string>> ListFlatAsync(string account, string container, string prefix, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        string? marker = null;
        do
        {
            var next = marker;
            var address = "/" + Uri.EscapeDataString(container) + Query(("restype", "container"), ("comp", "list"), ("prefix", prefix), ("marker", next));
            var document = await CallAsync("listing", account, address, cancellationToken).ConfigureAwait(false);
            foreach (var blob in document.Root?.Element("Blobs")?.Elements("Blob") ?? [])
            {
                var blobName = blob.Element("Name")?.Value;
                if (!string.IsNullOrEmpty(blobName))
                {
                    names.Add(blobName);
                }
            }

            marker = document.Root?.Element("NextMarker")?.Value is { Length: > 0 } token ? token : null;
        }
        while (marker is not null);

        return names;
    }

    public override Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => TransferAsync(sourceUrl, targetUrl, overwrite, delete: true);

    public override Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => TransferAsync(sourceUrl, targetUrl, overwrite, delete: false);

    private async Task TransferAsync(Url sourceUrl, Url targetUrl, bool overwrite, bool delete)
    {
        if (sourceUrl.PathType == PathType.Folder)
        {
            // Every blob under the source prefix, its marker included, goes to the same place under the target.
            var sourcePrefix = BlobNameOf(sourceUrl);
            var sourceFolder = sourcePrefix.Length == 0 ? string.Empty : sourcePrefix + "/";
            var targetBase = BlobNameOf(targetUrl);
            var names = await ListFlatAsync(AccountOf(sourceUrl), ContainerOf(sourceUrl), sourceFolder, CancellationToken.None).ConfigureAwait(false);

            foreach (var blob in names)
            {
                var relative = blob[sourceFolder.Length..];
                var targetName = targetBase.Length == 0 ? relative : targetBase + "/" + relative;
                if (blob.EndsWith('/'))
                {
                    // A folder marker is only a name: the target gets its own marker, and a move removes the source's.
                    if (targetName.Length > 0)
                    {
                        await CreateFolderAsync(SiblingUrl(targetUrl, targetName)).ConfigureAwait(false);
                    }

                    if (delete)
                    {
                        await DeleteBlobAsync(AccountOf(sourceUrl), ContainerOf(sourceUrl), blob, CancellationToken.None).ConfigureAwait(false);
                    }

                    continue;
                }

                await TransferOneAsync(SiblingUrl(sourceUrl, blob), SiblingUrl(targetUrl, targetName), overwrite, delete).ConfigureAwait(false);
            }

            return;
        }

        await TransferOneAsync(sourceUrl, targetUrl, overwrite, delete).ConfigureAwait(false);
    }

    /// <summary>The URL of <paramref name="blobName"/> in the same account and container as <paramref name="like"/>.</summary>
    private static Url SiblingUrl(Url like, string blobName) =>
        new(Container.AZURE_BLOB + AccountOf(like) + "/" + ContainerOf(like) + "/" + blobName);

    private async Task TransferOneAsync(Url sourceUrl, Url targetUrl, bool overwrite, bool delete)
    {
        if (!overwrite && await BlobExistsAsync(targetUrl, CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }

        var sourceAccount = AccountOf(sourceUrl);
        var targetAccount = AccountOf(targetUrl);
        if (string.Equals(sourceAccount, targetAccount, StringComparison.OrdinalIgnoreCase))
        {
            await ServerCopyAsync(sourceUrl, targetUrl).ConfigureAwait(false);
        }
        else
        {
            // A copy across accounts needs a SAS for the source, so the bytes come through this machine.
            var temp = Path.Combine(Path.GetTempPath(), "iTerminal-azure-" + Guid.NewGuid().ToString("N"));
            try
            {
                await using (var spool = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    await StreamToAsync(sourceUrl, spool, progress: null, CancellationToken.None).ConfigureAwait(false);
                }

                await using var spooled = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                await StreamFromAsync(targetUrl, spooled, progress: null, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(temp);
            }
        }

        if (delete)
        {
            await DeleteBlobAsync(sourceAccount, ContainerOf(sourceUrl), BlobNameOf(sourceUrl), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary><c>Copy Blob</c>: the service copies it. A copy it answers as pending is polled until it is done.</summary>
    private async Task ServerCopyAsync(Url sourceUrl, Url targetUrl)
    {
        var account = AccountOf(targetUrl);
        var source = BaseFor(AccountOf(sourceUrl)) + BlobPath(ContainerOf(sourceUrl), BlobNameOf(sourceUrl));
        var target = BlobPath(ContainerOf(targetUrl), BlobNameOf(targetUrl));
        string status;
        using (var response = await SendAsync(() =>
               {
                   var request = Request(Put, account, target);
                   request.Headers.TryAddWithoutValidation("x-ms-copy-source", source);
                   return request;
               }).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw Failure("copy", response, ErrorCode(await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
            }

            status = response.Headers.TryGetValues("x-ms-copy-status", out var values) ? values.First() : "success";
        }

        var deadline = DateTime.UtcNow.Add(CopyDeadline);
        while (status == "pending" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            using var poll = await HeadAsync(account, target).ConfigureAwait(false);
            status = poll.Headers.TryGetValues("x-ms-copy-status", out var pending) ? pending.First() : "success";
            if (status == "failed" || status == "aborted")
            {
                throw new IOException($"Azure Blob Storage copy: the service stopped the copy ({status}).");
            }
        }

        if (status == "pending")
        {
            throw new IOException("Azure Blob Storage copy: the service did not finish within ten minutes.");
        }
    }
}
