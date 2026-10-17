using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Web;

namespace iTerminal.Core.Services.Web;

/// <summary>One request of the Web Browser. <paramref name="Body"/> is sent form-encoded when the method is POST.</summary>
public sealed record WebFetchRequest(string Url, string Method = "GET", string? Body = null, string? Referer = null)
{
    public bool IsPost => string.Equals(Method, "POST", StringComparison.OrdinalIgnoreCase);
}

/// <summary>What came back: the address it finally came from (after redirects), the status and the raw bytes.</summary>
public sealed record WebFetchResponse(string Url, int Status, string ReasonPhrase, string ContentType, byte[] Content, string? FileName = null)
{
    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>The page could not be fetched at all (no network, bad address, refused certificate…). The message is meant to be read.</summary>
public sealed class WebFetchException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Fetches the pages of the Web Browser. The end-to-end suite replaces it, so no run reaches the network.</summary>
public interface IWebFetcher
{
    Task<WebFetchResponse> FetchAsync(WebFetchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches <c>http</c>, <c>https</c>, <c>file</c>, <c>data</c> and <c>about</c> addresses. One
/// <see cref="HttpClient"/> (and so one cookie jar) lives as long as the app, which is what lets a login
/// carry from page to page. Redirects are followed, compressed answers are decoded, and nothing bigger than
/// <see cref="MaxBytes"/> is read.
/// </summary>
public sealed class WebFetcher : IWebFetcher, IDisposable
{
    public const int MaxBytes = 64 * 1024 * 1024;
    public const string UserAgent = "Mozilla/5.0 (compatible; iTerminal-WebBrowser/1.0; +https://github.com/nagytam/iTerminal)";

    private readonly HttpClient _http;

    public WebFetcher()
        : this(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            UseCookies = true,
            CookieContainer = new CookieContainer(),
        })
    {
    }

    /// <summary>Tests pass a handler that answers from memory.</summary>
    public WebFetcher(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(45) };
    }

    public void Dispose() => _http.Dispose();

    public async Task<WebFetchResponse> FetchAsync(WebFetchRequest request, CancellationToken cancellationToken = default)
    {
        var url = request.Url.Trim();
        if (url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return About(url);
        }

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return Data(url);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new WebFetchException($"“{url}” is not a valid address.");
        }

        if (uri.IsFile)
        {
            return await Task.Run(() => LocalFile(uri), cancellationToken);
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new WebFetchException($"The “{uri.Scheme}” protocol is not supported. Supported: http, https, file, data.");
        }

        using var message = new HttpRequestMessage(request.IsPost ? HttpMethod.Post : HttpMethod.Get, uri);
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        message.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,text/plain;q=0.8,*/*;q=0.5");
        message.Headers.TryAddWithoutValidation("Accept-Language", System.Globalization.CultureInfo.CurrentUICulture.Name + ",en;q=0.8");
        if (request.Referer is { Length: > 0 } referer && Uri.TryCreate(referer, UriKind.Absolute, out var referrerUri) && referrerUri.Scheme is "http" or "https")
        {
            message.Headers.Referrer = referrerUri;
        }

        if (request.IsPost)
        {
            message.Content = new StringContent(request.Body ?? string.Empty, Encoding.UTF8, new MediaTypeHeaderValue("application/x-www-form-urlencoded"));
        }

        try
        {
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var finalUrl = response.RequestMessage?.RequestUri?.AbsoluteUri ?? uri.AbsoluteUri;
            var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                throw new WebFetchException($"The page is larger than {MaxBytes / (1024 * 1024)} MB and was not loaded.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                {
                    throw new WebFetchException($"The page is larger than {MaxBytes / (1024 * 1024)} MB and was not loaded.");
                }
            }

            return new WebFetchResponse(finalUrl, (int)response.StatusCode, response.ReasonPhrase ?? string.Empty, contentType, buffer.ToArray(), fileName?.Trim('"'));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new WebFetchException($"{uri.Host} took too long to answer.");
        }
        catch (HttpRequestException exception)
        {
            throw new WebFetchException(Describe(uri, exception), exception);
        }
    }

    private static string Describe(Uri uri, HttpRequestException exception)
    {
        var inner = exception.InnerException;
        var detail = inner?.Message ?? exception.Message;
        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => $"{uri.Host} could not be found. Check the address and your connection.",
            HttpRequestError.ConnectionError => $"Could not connect to {uri.Host}: {detail}",
            HttpRequestError.SecureConnectionError => $"The secure connection to {uri.Host} failed: {detail}",
            _ => detail,
        };
    }

    private static WebFetchResponse About(string url)
    {
        var page = url.ToLowerInvariant() switch
        {
            "about:blank" => string.Empty,
            _ => WebHomePage.Html,
        };
        return new WebFetchResponse(url, 200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(page));
    }

    private static WebFetchResponse Data(string url)
    {
        var comma = url.IndexOf(',');
        if (comma < 0)
        {
            throw new WebFetchException("The data address is malformed.");
        }

        var header = url[5..comma];
        var payload = url[(comma + 1)..];
        var isBase64 = header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        var contentType = (isBase64 ? header[..^7] : header);
        if (contentType.Length == 0)
        {
            contentType = "text/plain;charset=US-ASCII";
        }

        try
        {
            var bytes = isBase64
                ? Convert.FromBase64String(Uri.UnescapeDataString(payload))
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
            return new WebFetchResponse(url, 200, "OK", contentType, bytes);
        }
        catch (FormatException)
        {
            throw new WebFetchException("The data address is malformed.");
        }
    }

    private static WebFetchResponse LocalFile(Uri uri)
    {
        var path = uri.LocalPath;
        try
        {
            if (Directory.Exists(path))
            {
                return new WebFetchResponse(uri.AbsoluteUri, 200, "OK", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(DirectoryListing(path)));
            }

            if (!File.Exists(path))
            {
                throw new WebFetchException($"The file {path} does not exist.");
            }

            var info = new FileInfo(path);
            if (info.Length > MaxBytes)
            {
                throw new WebFetchException($"The file is larger than {MaxBytes / (1024 * 1024)} MB and was not loaded.");
            }

            return new WebFetchResponse(uri.AbsoluteUri, 200, "OK", ContentTypeOf(path), File.ReadAllBytes(path), info.Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new WebFetchException(exception.Message, exception);
        }
    }

    private static string DirectoryListing(string path)
    {
        var builder = new StringBuilder();
        builder.Append("<html><head><title>").Append(HttpUtility.HtmlEncode(path)).Append("</title></head><body><h1>")
            .Append(HttpUtility.HtmlEncode(path)).Append("</h1><ul>");
        var parent = Directory.GetParent(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (parent is not null)
        {
            builder.Append("<li><a href=\"").Append(HttpUtility.HtmlAttributeEncode(new Uri(parent.FullName + Path.DirectorySeparatorChar).AbsoluteUri)).Append("\">..</a></li>");
        }

        foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos().OrderByDescending(e => e is DirectoryInfo).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).Take(5000))
        {
            var isDirectory = entry is DirectoryInfo;
            var target = new Uri(entry.FullName + (isDirectory ? Path.DirectorySeparatorChar : string.Empty)).AbsoluteUri;
            builder.Append("<li><a href=\"").Append(HttpUtility.HtmlAttributeEncode(target)).Append("\">")
                .Append(HttpUtility.HtmlEncode(entry.Name + (isDirectory ? "/" : string.Empty))).Append("</a></li>");
        }

        builder.Append("</ul></body></html>");
        return builder.ToString();
    }

    private static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" or ".xhtml" => "text/html",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".svg" => "image/svg+xml",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        ".txt" or ".md" or ".log" or ".cs" or ".json" or ".xml" or ".csv" or ".css" or ".js" or ".ts" or ".yml" or ".yaml" or ".ini" or ".config" or ".sh" or ".ps1" or ".cmd" or ".bat" or ".py" => "text/plain",
        _ => "application/octet-stream",
    };
}

/// <summary>The page <c>about:home</c> shows: a search box and the keys. It needs no network to open.</summary>
internal static class WebHomePage
{
    public const string Html = """
        <html><head><title>Web Browser</title></head><body>
        <h1>Web Browser</h1>
        <p>A text-mode browser for iTerminal. Type an address or a search into the address bar (<b>Ctrl+L</b> or <b>F6</b>), or search here:</p>
        <form action="https://html.duckduckgo.com/html/" method="get"><input type="text" name="q" size="40" placeholder="Search the web"> <input type="submit" value="Search"></form>
        <h2>Keys</h2>
        <ul>
        <li><b>Tab</b> / <b>Shift+Tab</b> – next / previous link or form control; <b>Enter</b> opens or edits it</li>
        <li><b>Up</b> <b>Down</b> <b>PgUp</b> <b>PgDn</b> <b>Home</b> <b>End</b> – scroll</li>
        <li><b>Alt+Left</b> / <b>Backspace</b> – back; <b>Alt+Right</b> – forward; <b>F5</b> – reload; <b>Esc</b> – stop loading, then close</li>
        <li><b>Ctrl+T</b> new tab, <b>Ctrl+W</b> close tab, <b>Ctrl+PgUp</b>/<b>Ctrl+PgDn</b> or <b>Alt+1…9</b> switch tab</li>
        <li><b>Ctrl+D</b> bookmark, <b>F2</b> bookmarks, <b>F4</b> history, <b>Ctrl+F</b> find, <b>F9</b> page source, <b>F7</b> save, <b>F1</b> help</li>
        </ul>
        <p>Pages are shown as text: there is no JavaScript, and images appear as their description.</p>
        </body></html>
        """;
}
