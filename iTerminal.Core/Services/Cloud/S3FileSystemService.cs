using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;

namespace iTerminal.Core.Services.Cloud;

/// <summary>The <c>s3://</c> drive's back-end.</summary>
public interface IS3FileSystemService : IEntryListingFileSystem, IDisposable
{
    /// <summary>
    /// Points the service at an account. Null keys make anonymous (unsigned) requests, which is how a
    /// public bucket is read. <paramref name="endpoint"/> is an S3-compatible server (MinIO, Ceph,
    /// Cloudflare R2, Backblaze B2…); null means Amazon S3. Nothing is sent: the first request is the first conversation.
    /// </summary>
    void Connect(string? accessKeyId, string? secretAccessKey, string? sessionToken, string? region, string? endpoint);

    bool IsConnected { get; }

    void Disconnect();
}

/// <summary>
/// The S3 drive (<c>s3://[accessKeyId@]bucket/key</c>) written against the REST API with
/// <see cref="HttpClient"/> and <see cref="AwsSignatureV4"/>: ListObjectsV2 with a <c>/</c> delimiter
/// is what makes a flat key space look like folders, <c>PUT</c> of a zero-byte key ending in <c>/</c>
/// is how a folder is made, and a folder delete or move is a walk over every key under the prefix —
/// S3 has no rename.
///
/// <para>The bucket is the URL's host, so one connection serves every bucket the keys can reach, and an
/// empty host lists them. A bucket in another region answers with the right one in
/// <c>x-amz-bucket-region</c>; the region is learned per bucket and the request repeated. Amazon
/// buckets are addressed virtual-hosted where the name allows it, and every custom endpoint
/// path-style (<c>https://host/bucket/key</c>), which is what MinIO and friends expect.</para>
/// </summary>
public class S3FileSystemService : HttpFileSystemServiceBase, IS3FileSystemService
{
    private static readonly HttpRequestOptionsKey<SigningInfo> SigningKey = new("iTerminal.S3.Signing");
    private static readonly HttpMethod Head = HttpMethod.Head;

    private readonly ConcurrentDictionary<string, string> _bucketRegions = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private string? _accessKeyId;
    private string? _secretAccessKey;
    private string? _sessionToken;
    private string _region = "us-east-1";
    private Uri? _endpoint;
    private string _key = string.Empty;
    private volatile bool _connected;

    public S3FileSystemService(ILogger? logger = null)
        : this(new HttpClientHandler { AllowAutoRedirect = false }, logger)
    {
    }

    /// <param name="handler">The handler requests go through; replaced by a fake in tests. It should not follow redirects.</param>
    public S3FileSystemService(HttpMessageHandler handler, ILogger? logger = null)
        : base(handler, logger)
    {
    }

    protected override string ServiceName => "S3";

    public bool IsConnected => _connected;

    public void Connect(string? accessKeyId, string? secretAccessKey, string? sessionToken, string? region, string? endpoint)
    {
        var key = string.Join('|', accessKeyId, region, endpoint);
        lock (_gate)
        {
            _accessKeyId = string.IsNullOrEmpty(accessKeyId) || string.IsNullOrEmpty(secretAccessKey) ? null : accessKeyId;
            _secretAccessKey = _accessKeyId is null ? null : secretAccessKey;
            _sessionToken = string.IsNullOrEmpty(sessionToken) ? null : sessionToken;
            _region = string.IsNullOrWhiteSpace(region) ? "us-east-1" : region.Trim();
            _endpoint = !string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(
                    endpoint.Contains("://", StringComparison.Ordinal) ? endpoint.Trim() : "https://" + endpoint.Trim(),
                    UriKind.Absolute,
                    out var parsed)
                ? parsed
                : null;
            if (_key != key)
            {
                _bucketRegions.Clear();
            }

            _key = key;
            _connected = true;
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            _connected = false;
            _accessKeyId = null;
            _secretAccessKey = null;
            _sessionToken = null;
            _key = string.Empty;
            _bucketRegions.Clear();
        }
    }

    // --- addressing ---------------------------------------------------------------------------

    private sealed record SigningInfo(string Path, IReadOnlyList<KeyValuePair<string, string>> Query, string Region, string Host);

    private static (string Bucket, string Key) Split(Url url) => (url.Host, url.Path.TrimStart('/'));

    internal static string EscapeKey(string key) =>
        string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    private static bool IsDnsCompatible(string bucket) =>
        bucket.Length is >= 3 and <= 63
        && bucket.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
        && bucket[0] != '-' && bucket[^1] != '-';

    private string RegionOf(string bucket) =>
        bucket.Length > 0 && _bucketRegions.TryGetValue(bucket, out var region) ? region : _region;

    private HttpRequestMessage Build(
        HttpMethod method,
        string bucket,
        string key,
        string region,
        IReadOnlyList<KeyValuePair<string, string>>? query = null,
        string payloadHash = AwsSignatureV4.EmptyPayloadHash)
    {
        string scheme;
        string host;
        bool pathStyle;
        if (_endpoint is not null)
        {
            scheme = _endpoint.Scheme;
            host = _endpoint.Authority;
            pathStyle = true;
        }
        else
        {
            var regional = region == "us-east-1" ? "s3.amazonaws.com" : $"s3.{region}.amazonaws.com";
            scheme = "https";
            if (bucket.Length > 0 && IsDnsCompatible(bucket))
            {
                host = bucket + "." + regional;
                pathStyle = false;
            }
            else
            {
                host = regional;
                pathStyle = true;
            }
        }

        var path = !pathStyle
            ? "/" + EscapeKey(key)
            : bucket.Length == 0
                ? "/"
                : "/" + Uri.EscapeDataString(bucket) + "/" + EscapeKey(key);

        query ??= [];
        var queryText = query.Count == 0
            ? string.Empty
            : "?" + string.Join('&', query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        var request = new HttpRequestMessage(method, scheme + "://" + host + path + queryText);
        request.Options.Set(SigningKey, new SigningInfo(path, query, region, host));
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (_sessionToken is not null)
        {
            request.Headers.TryAddWithoutValidation("x-amz-security-token", _sessionToken);
        }

        return request;
    }

    protected override Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? accessKey;
        string? secret;
        lock (_gate)
        {
            accessKey = _accessKeyId;
            secret = _secretAccessKey;
        }

        if (accessKey is null || secret is null || !request.Options.TryGetValue(SigningKey, out var info))
        {
            return Task.CompletedTask;
        }

        var now = DateTime.UtcNow;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["host"] = info.Host,
            ["x-amz-date"] = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture),
        };
        foreach (var header in request.Headers)
        {
            if (header.Key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase))
            {
                headers[header.Key] = string.Join(',', header.Value);
            }
        }

        var payloadHash = headers.GetValueOrDefault("x-amz-content-sha256") ?? AwsSignatureV4.EmptyPayloadHash;
        var authorization = AwsSignatureV4.Authorize(
            request.Method.Method, info.Path, info.Query, headers, payloadHash,
            accessKey, secret, info.Region, "s3", now, out _, out _);
        request.Headers.TryAddWithoutValidation("x-amz-date", headers["x-amz-date"]);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends against <paramref name="bucket"/> in the region it is known to be in. A response that
    /// names another region (a redirect, or "the authorization header is malformed" for the wrong one)
    /// teaches the region and repeats the request once.
    /// </summary>
    private async Task<HttpResponseMessage> SendS3Async(
        string bucket,
        Func<string, HttpRequestMessage> build,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        for (var pass = 0; ; pass++)
        {
            var region = RegionOf(bucket);
            var response = await SendAsync(() => build(region), completion, cancellationToken).ConfigureAwait(false);
            if (pass == 0
                && bucket.Length > 0
                && response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect
                    or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden
                && response.Headers.TryGetValues("x-amz-bucket-region", out var regions)
                && regions.FirstOrDefault() is { Length: > 0 } actual
                && !string.Equals(actual, region, StringComparison.Ordinal))
            {
                _bucketRegions[bucket] = actual;
                response.Dispose();
                continue;
            }

            return response;
        }
    }

    private async Task<IOException> ErrorAsync(string what, HttpResponseMessage response)
    {
        string? detail = null;
        try
        {
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (text.Length > 0 && text.TrimStart().StartsWith('<'))
            {
                var document = XDocument.Parse(text);
                var code = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Code")?.Value;
                var message = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value;
                detail = string.Join(": ", new[] { code, message }.Where(v => !string.IsNullOrEmpty(v)));
            }
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or HttpRequestException)
        {
            // The status code alone will have to do.
        }

        return Failure(what, response, detail);
    }

    // --- listing ------------------------------------------------------------------------------

    private string BucketRoot(Url url, string bucket)
    {
        var user = string.IsNullOrEmpty(url.Username) ? string.Empty : url.Username + "@";
        return Container.S3 + user + bucket + "/";
    }

    private static IEnumerable<XElement> Elements(XDocument document, string localName) =>
        document.Descendants().Where(e => e.Name.LocalName == localName);

    private static string? Child(XElement element, string localName) =>
        element.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;

    private static DateTime? ParseDate(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.LocalDateTime
            : null;

    public override async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            throw new IOException("Not connected to S3.");
        }

        var (bucket, key) = Split(url);
        var rows = new List<FileSystemEntry>();
        if (bucket.Length == 0)
        {
            using var response = await SendS3Async(string.Empty, region => Build(HttpMethod.Get, string.Empty, string.Empty, region), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("listing buckets", response).ConfigureAwait(false);
            }

            var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            foreach (var bucketElement in Elements(document, "Bucket"))
            {
                var name = Child(bucketElement, "Name");
                if (!string.IsNullOrEmpty(name))
                {
                    rows.Add(BuildEntry(BucketRoot(url, string.Empty).TrimEnd('/'), name, isFolder: true, 0, ParseDate(Child(bucketElement, "CreationDate"))));
                }
            }

            return Arrange(rows);
        }

        var prefix = key.Length == 0 || key.EndsWith('/') ? key : key + "/";
        var root = BucketRoot(url, bucket);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        do
        {
            var query = new List<KeyValuePair<string, string>>
            {
                new("list-type", "2"),
                new("delimiter", "/"),
                new("max-keys", "1000"),
            };
            if (prefix.Length > 0)
            {
                query.Add(new("prefix", prefix));
            }

            if (token is not null)
            {
                query.Add(new("continuation-token", token));
            }

            using var response = await SendS3Async(bucket, region => Build(HttpMethod.Get, bucket, string.Empty, region, query), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("listing", response).ConfigureAwait(false);
            }

            var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            foreach (var commonPrefix in Elements(document, "CommonPrefixes"))
            {
                var full = Child(commonPrefix, "Prefix");
                if (full is null || full.Length <= prefix.Length)
                {
                    continue;
                }

                var name = full[prefix.Length..].TrimEnd('/');
                if (name.Length > 0 && seen.Add(name + "/"))
                {
                    rows.Add(BuildEntry(root + prefix, name, isFolder: true, 0, null));
                }
            }

            foreach (var content in Elements(document, "Contents"))
            {
                var full = Child(content, "Key");
                if (full is null || full.Length <= prefix.Length || full.EndsWith('/'))
                {
                    continue;
                }

                var name = full[prefix.Length..];
                if (name.Contains('/', StringComparison.Ordinal) || !seen.Add(name))
                {
                    continue;
                }

                long.TryParse(Child(content, "Size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size);
                rows.Add(BuildEntry(root + prefix, name, isFolder: false, size, ParseDate(Child(content, "LastModified"))));
            }

            var truncated = string.Equals(Elements(document, "IsTruncated").FirstOrDefault()?.Value, "true", StringComparison.OrdinalIgnoreCase);
            token = truncated ? Elements(document, "NextContinuationToken").FirstOrDefault()?.Value : null;
        }
        while (token is not null);

        return Arrange(rows);
    }

    /// <summary>Every key under <paramref name="prefix"/>, with no delimiter — what a folder delete, copy or move walks.</summary>
    private async Task<List<string>> ListAllKeysAsync(string bucket, string prefix, CancellationToken cancellationToken)
    {
        var keys = new List<string>();
        string? token = null;
        do
        {
            var query = new List<KeyValuePair<string, string>> { new("list-type", "2"), new("max-keys", "1000") };
            if (prefix.Length > 0)
            {
                query.Add(new("prefix", prefix));
            }

            if (token is not null)
            {
                query.Add(new("continuation-token", token));
            }

            using var response = await SendS3Async(bucket, region => Build(HttpMethod.Get, bucket, string.Empty, region, query), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw await ErrorAsync("listing", response).ConfigureAwait(false);
            }

            var document = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            keys.AddRange(Elements(document, "Contents").Select(c => Child(c, "Key")).Where(k => !string.IsNullOrEmpty(k))!);
            var truncated = string.Equals(Elements(document, "IsTruncated").FirstOrDefault()?.Value, "true", StringComparison.OrdinalIgnoreCase);
            token = truncated ? Elements(document, "NextContinuationToken").FirstOrDefault()?.Value : null;
        }
        while (token is not null);

        return keys;
    }

    // --- existence ----------------------------------------------------------------------------

    private bool Probe(Func<Task<bool>> ask)
    {
        if (!IsConnected)
        {
            return false;
        }

        try
        {
            return ask().GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException)
        {
            Logger?.Warning(exception, "S3 exists check failed");
            return false;
        }
    }

    public override bool ExistsFile(Url url) => Probe(async () =>
    {
        var (bucket, key) = Split(url);
        if (bucket.Length == 0 || key.Length == 0)
        {
            return false;
        }

        using var response = await SendS3Async(bucket, region => Build(Head, bucket, key, region)).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    });

    public override bool ExistsFolder(Url url) => Probe(async () =>
    {
        var (bucket, key) = Split(url);
        if (bucket.Length == 0)
        {
            return true;
        }

        if (key.Length == 0)
        {
            using var head = await SendS3Async(bucket, region => Build(Head, bucket, string.Empty, region)).ConfigureAwait(false);
            return head.IsSuccessStatusCode;
        }

        var prefix = key.EndsWith('/') ? key : key + "/";
        var query = new List<KeyValuePair<string, string>> { new("list-type", "2"), new("max-keys", "1"), new("prefix", prefix) };
        using var response = await SendS3Async(bucket, region => Build(HttpMethod.Get, bucket, string.Empty, region, query)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var document = XDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return Elements(document, "Contents").Any();
    });

    // --- transfer -----------------------------------------------------------------------------

    protected override async Task<HttpResponseMessage?> OpenDownloadAsync(Url url, CancellationToken cancellationToken)
    {
        var (bucket, key) = Split(url);
        var response = await SendS3Async(bucket, region => Build(HttpMethod.Get, bucket, key, region), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
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

    private const long SinglePutLimit = 5L * 1024 * 1024 * 1024;

    protected override async Task UploadAsync(Url url, Stream source, long length, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (bucket, key) = Split(url);
        if (bucket.Length == 0 || key.Length == 0)
        {
            throw new IOException("S3: a file goes in a bucket, under a name.");
        }

        if (length > SinglePutLimit)
        {
            throw new IOException("S3: a single upload is limited to 5 GB.");
        }

        var start = source.Position;
        var tls = (_endpoint?.Scheme ?? "https") == "https";
        var payloadHash = tls ? AwsSignatureV4.UnsignedPayload : await AwsSignatureV4.Sha256HexAsync(source, cancellationToken).ConfigureAwait(false);

        using var response = await SendS3Async(bucket, region =>
        {
            source.Position = start;
            var request = Build(HttpMethod.Put, bucket, key, region, null, payloadHash);
            request.Content = StreamBody(source, length, progress);
            return request;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("upload", response).ConfigureAwait(false);
        }
    }

    // --- folders, delete, copy, move ----------------------------------------------------------

    public override async Task CreateFolderAsync(Url url)
    {
        var (bucket, key) = Split(url);
        if (bucket.Length == 0 || key.Length == 0)
        {
            throw new IOException("S3: buckets are created in the provider's console, not here.");
        }

        var folderKey = key.EndsWith('/') ? key : key + "/";
        using var response = await SendS3Async(bucket, region => Build(HttpMethod.Put, bucket, folderKey, region)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("create folder", response).ConfigureAwait(false);
        }
    }

    private async Task DeleteKeyAsync(string bucket, string key)
    {
        using var response = await SendS3Async(bucket, region => Build(HttpMethod.Delete, bucket, key, region)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            throw await ErrorAsync("delete", response).ConfigureAwait(false);
        }
    }

    // There is no bin to move to; the flag is ignored, as on the other remote drives.
    public override bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var (bucket, key) = Split(url);
        if (bucket.Length == 0 || key.Length == 0)
        {
            return false;
        }

        DeleteKeyAsync(bucket, key).GetAwaiter().GetResult();
        return true;
    }

    public override bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        var (bucket, key) = Split(url);
        if (bucket.Length == 0 || key.Length == 0)
        {
            return false;
        }

        var prefix = key.EndsWith('/') ? key : key + "/";
        foreach (var existing in ListAllKeysAsync(bucket, prefix, CancellationToken.None).GetAwaiter().GetResult())
        {
            DeleteKeyAsync(bucket, existing).GetAwaiter().GetResult();
        }

        // The placeholder some tools leave for an empty folder is one of the keys above when present.
        return true;
    }

    private async Task CopyKeyAsync(string sourceBucket, string sourceKey, string targetBucket, string targetKey)
    {
        var copySource = "/" + Uri.EscapeDataString(sourceBucket) + "/" + EscapeKey(sourceKey);
        using var response = await SendS3Async(targetBucket, region =>
        {
            var request = Build(HttpMethod.Put, targetBucket, targetKey, region);
            request.Headers.TryAddWithoutValidation("x-amz-copy-source", copySource);
            return request;
        }).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await ErrorAsync("copy", response).ConfigureAwait(false);
        }

        // A copy can fail after the 200 has been sent; the error is then in the body.
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (body.Contains("<Error>", StringComparison.Ordinal))
        {
            var message = XDocument.Parse(body).Descendants().FirstOrDefault(e => e.Name.LocalName == "Message")?.Value;
            throw new IOException($"S3 copy: {message ?? "the copy failed"}.");
        }
    }

    private async Task TransferAsync(Url sourceUrl, Url targetUrl, bool overwrite, bool delete)
    {
        var (sourceBucket, sourceKey) = Split(sourceUrl);
        var (targetBucket, targetKey) = Split(targetUrl);
        if (sourceBucket.Length == 0 || sourceKey.Length == 0 || targetBucket.Length == 0 || targetKey.Length == 0)
        {
            throw new IOException("S3: a bucket cannot be copied or moved here.");
        }

        if (sourceUrl.PathType == PathType.Folder || sourceKey.EndsWith('/'))
        {
            var sourcePrefix = sourceKey.EndsWith('/') ? sourceKey : sourceKey + "/";
            var targetPrefix = targetKey.EndsWith('/') ? targetKey : targetKey + "/";
            var keys = await ListAllKeysAsync(sourceBucket, sourcePrefix, CancellationToken.None).ConfigureAwait(false);
            foreach (var key in keys)
            {
                var destination = targetPrefix + key[sourcePrefix.Length..];
                if (!overwrite && await Exists(targetBucket, destination).ConfigureAwait(false))
                {
                    continue;
                }

                await CopyKeyAsync(sourceBucket, key, targetBucket, destination).ConfigureAwait(false);
                if (delete)
                {
                    await DeleteKeyAsync(sourceBucket, key).ConfigureAwait(false);
                }
            }

            return;
        }

        if (!overwrite && await Exists(targetBucket, targetKey).ConfigureAwait(false))
        {
            return;
        }

        await CopyKeyAsync(sourceBucket, sourceKey, targetBucket, targetKey).ConfigureAwait(false);
        if (delete)
        {
            await DeleteKeyAsync(sourceBucket, sourceKey).ConfigureAwait(false);
        }
    }

    private async Task<bool> Exists(string bucket, string key)
    {
        using var response = await SendS3Async(bucket, region => Build(Head, bucket, key, region)).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public override Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => TransferAsync(sourceUrl, targetUrl, overwrite, delete: true);

    public override Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => TransferAsync(sourceUrl, targetUrl, overwrite, delete: false);
}
