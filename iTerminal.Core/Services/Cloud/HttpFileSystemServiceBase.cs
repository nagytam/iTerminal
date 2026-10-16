using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using Serilog;

namespace iTerminal.Core.Services.Cloud;

/// <summary>
/// What the HTTP-backed drives that are not WebDAV (S3, OneDrive, Google Drive) have in common: one
/// <see cref="HttpClient"/> for the run, a send that signs each request and retries what is worth
/// retrying (an expired token once, throttling with back-off), and the transfer plumbing —
/// downloads stream straight to the destination, uploads are spooled to a temporary file first so
/// the length is known and a retry can start over.
///
/// <para>Subclasses supply the protocol: <see cref="AuthorizeAsync"/> (a bearer token, a signature),
/// <see cref="List"/>, <see cref="OpenDownloadAsync"/> and <see cref="UploadAsync"/>, plus
/// the folder operations. Everything is synchronous where <see cref="IFileSystem"/> is and is only
/// ever called from a background task, the way the WebDAV drive's is.</para>
/// </summary>
public abstract class HttpFileSystemServiceBase(HttpMessageHandler? handler, ILogger? logger)
    : IEntryListingFileSystem, IDisposable
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _http = handler is null
        ? new HttpClient { Timeout = TimeSpan.FromMinutes(30) }
        : new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(30) };

    protected ILogger? Logger { get; } = logger;

    /// <summary>The client; subclasses use it for requests that must not be authorized by <see cref="AuthorizeAsync"/> (an upload session's own URL).</summary>
    protected HttpClient Http => _http;

    public bool IsReadOnly => false;

    /// <summary>Free space where the service knows it from a cache; never asks the network (it is drawn on every repaint).</summary>
    public virtual string GetAvailableDiskSpace() => string.Empty;

    /// <summary>The rows of <paramref name="url"/>, without a <c>..</c> row (the panel adds that), with size and date.</summary>
    public abstract Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);

    public abstract bool ExistsFile(Url url);

    public abstract bool ExistsFolder(Url url);

    public abstract Task CreateFolderAsync(Url url);

    public abstract bool DeleteFile(Url url, bool moveToRecycleBin);

    public abstract bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin);

    public abstract Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite);

    public abstract Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite);

    /// <summary>Adds whatever makes <paramref name="request"/> acceptable to the service.</summary>
    protected abstract Task AuthorizeAsync(HttpRequestMessage request, CancellationToken cancellationToken);

    /// <summary>
    /// Called after a 401. True when something was done that makes another attempt worthwhile (the
    /// token was refreshed); the default is that there is nothing to try.
    /// </summary>
    protected virtual Task<bool> OnUnauthorizedAsync(CancellationToken cancellationToken) => Task.FromResult(false);

    /// <summary>Starts the download of <paramref name="url"/>; the caller disposes the response. Null when there is no such file.</summary>
    protected abstract Task<HttpResponseMessage?> OpenDownloadAsync(Url url, CancellationToken cancellationToken);

    /// <summary>Uploads <paramref name="source"/> (seekable, <paramref name="length"/> bytes) to <paramref name="url"/>, replacing what is there.</summary>
    protected abstract Task UploadAsync(Url url, Stream source, long length, IProgress<long>? progress, CancellationToken cancellationToken);

    /// <summary>What a failed call is called in a message: "OneDrive", "Google Drive", "S3".</summary>
    protected abstract string ServiceName { get; }

    /// <summary>Sends a request built by <paramref name="build"/> (called again for each attempt), with the retries described above.</summary>
    protected async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> build,
        HttpCompletionOption completion = HttpCompletionOption.ResponseContentRead,
        CancellationToken cancellationToken = default)
    {
        var refreshedToken = false;
        for (var attempt = 1; ; attempt++)
        {
            using var request = build();
            await AuthorizeAsync(request, cancellationToken).ConfigureAwait(false);
            var response = await _http.SendAsync(request, completion, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedToken)
            {
                refreshedToken = true;
                if (await OnUnauthorizedAsync(cancellationToken).ConfigureAwait(false))
                {
                    response.Dispose();
                    continue;
                }
            }

            var throttled = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;
            if (throttled && attempt < MaxAttempts)
            {
                var delay = response.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                response.Dispose();
                await Task.Delay(delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            return response;
        }
    }

    /// <summary>A short, human reason for a failed response — never the whole body, which is often JSON.</summary>
    protected IOException Failure(string what, HttpResponseMessage response, string? detail = null)
    {
        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "the sign-in was refused or has expired",
            HttpStatusCode.Forbidden => "access was refused",
            HttpStatusCode.NotFound => "it is not there",
            HttpStatusCode.Conflict => "something is already there, or the folder it belongs in is missing",
            HttpStatusCode.TooManyRequests => "the service is throttling requests; try again in a moment",
            HttpStatusCode.InsufficientStorage => "the account is out of space",
            _ => $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
        };
        return new IOException($"{ServiceName} {what}: {reason}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : " (" + detail + ")")}.");
    }

    protected static void EnsureSuccess(HttpResponseMessage response, Func<HttpResponseMessage, IOException> failure)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw failure(response);
        }
    }

    protected static string UrlFolder(Url folder) =>
        folder.FullUrl.EndsWith('/') ? folder.FullUrl : folder.FullUrl + "/";

    /// <summary>One row for a listing: sizes and dates as the panels draw them.</summary>
    protected static FileSystemEntry BuildEntry(string folderUrl, string name, bool isFolder, long size, DateTime? modified)
    {
        var childUrl = new Url(folderUrl + name + (isFolder ? "/" : string.Empty), name);
        return new FileSystemEntry(childUrl)
        {
            FileSize = isFolder ? Url.Folder : size.ToFileSize(),
            Date = modified?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            Time = modified?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    /// <summary>The rows of a folder in the order the drives show them: folders first, then by name.</summary>
    protected static IReadOnlyList<IEntry> Arrange(IEnumerable<FileSystemEntry> rows) =>
        rows.OrderBy(r => r.Url.PathType)
            .ThenBy(r => r.Url.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Cast<IEntry>()
            .ToList();

    /// <summary>
    /// The children of a folder, for the walkers of a copy or a delete. They run on a background
    /// task, so asking the network and waiting is what they are for.
    /// </summary>
    public IEnumerable<Url> ListChildren(Url folderUrl) =>
        ListAsync(folderUrl, CancellationToken.None).GetAwaiter().GetResult().Select(e => e.Url).ToList();

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var response = await OpenDownloadAsync(url, CancellationToken.None).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return new MemoryFile(url, bytes);
    }

    public async Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        inputStream.Position = 0;
        await UploadAsync(url, inputStream, inputStream.Length, null, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var response = await OpenDownloadAsync(url, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"{ServiceName}: {url.FullUrl} is not there.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            total += read;
            progress?.Report(total);
        }
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // The services need the length up front (a signature, a Content-Range), and a failed attempt
        // has to be able to start from the first byte again: a stream of unknown length is spooled.
        if (source.CanSeek)
        {
            var remaining = source.Length - source.Position;
            await UploadAsync(url, source, remaining, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        var temp = Path.Combine(Path.GetTempPath(), "iTerminal-upload-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using (var spool = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await source.CopyToAsync(spool, cancellationToken).ConfigureAwait(false);
            }

            await using var spooled = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
            await UploadAsync(url, spooled, spooled.Length, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A content that reads <paramref name="length"/> bytes of a seekable stream from where it is and reports how many went.</summary>
    protected static HttpContent StreamBody(Stream source, long length, IProgress<long>? progress, string? contentType = null)
    {
        var content = new StreamContent(new LimitedProgressStream(source, length, progress));
        content.Headers.ContentLength = length;
        if (contentType is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return content;
    }

    /// <summary>Exposes at most <c>length</c> bytes of <c>inner</c> and reports the running total — which, for an upload, is how much has been sent.</summary>
    private sealed class LimitedProgressStream(Stream inner, long length, IProgress<long>? progress) : Stream
    {
        private long _read;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var take = (int)Math.Min(count, length - _read);
            if (take <= 0)
            {
                return 0;
            }

            var read = inner.Read(buffer, offset, take);
            Report(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var take = (int)Math.Min(buffer.Length, length - _read);
            if (take <= 0)
            {
                return 0;
            }

            var read = await inner.ReadAsync(buffer[..take], cancellationToken).ConfigureAwait(false);
            Report(read);
            return read;
        }

        private void Report(int read)
        {
            if (read > 0)
            {
                _read += read;
                progress?.Report(_read);
            }
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public virtual void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
