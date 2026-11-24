using System.Net;
using System.Security.Cryptography;
using System.Text;
#pragma warning disable S1075 // iTerminal's own URLs (not file-system paths) use '/' on every platform.

namespace iTerminal.Core.Services.Sharing;

/// <summary>
/// Answers one HTTP request for a file share: the token check, the path rules, the folder listing and the file
/// download. It works on streams, so the rules are tested without a socket. Only local folders and files are served;
/// the selection is fixed when the share starts.
/// </summary>
internal sealed class FileShareRequestHandler
{
    private const int MaxConcurrentReads = 4;

    private readonly IReadOnlyList<string> _roots;
    private readonly byte[] _token;

    public FileShareRequestHandler(IEnumerable<string> selectedPaths, string token)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ArgumentException.ThrowIfNullOrEmpty(token);
        _roots = selectedPaths.Select(Path.GetFullPath).ToList();
        _token = Encoding.UTF8.GetBytes(token);
    }

    /// <summary>Limits the file reads in flight; a read past the cap is answered 503 and not queued.</summary>
    internal SemaphoreSlim ReadSlots { get; } = new(MaxConcurrentReads, MaxConcurrentReads);

    /// <summary>Writes the response to <paramref name="output"/> and returns its status code.</summary>
    public async Task<int> WriteResponseAsync(string method, string target, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (method is not ("GET" or "HEAD"))
        {
            return await WriteStatusAsync(output, 405, "Method Not Allowed", "Allow: GET, HEAD", cancellationToken);
        }

        if (target.Contains('\\') || target.Contains('\0') || !target.StartsWith('/'))
        {
            return await WriteStatusAsync(output, 400, "Bad Request", null, cancellationToken);
        }

        string path = target;
        int cut = path.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            path = path[..cut];
        }

        string[] raw = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length == 0 || !TokenMatches(raw[0]))
        {
            return await WriteStatusAsync(output, 404, "Not Found", null, cancellationToken);
        }

        // Each segment is decoded once. A decoded separator, NUL or dot segment is refused.
        string[] segments = raw[1..].Select(Uri.UnescapeDataString).ToArray();
        foreach (string segment in segments)
        {
            if (segment.Contains('/') || segment.Contains('\\') || segment.Contains('\0') || segment is "." or "..")
            {
                return await WriteStatusAsync(output, 404, "Not Found", null, cancellationToken);
            }
        }

        if (segments.Length == 0)
        {
            List<string> names = _roots.Where(IsPlainPath).Select(Path.GetFileName).Where(name => name is not null).Select(name => name!).ToList();
            return await WriteListingAsync(output, names, method, cancellationToken);
        }

        string? root = _roots.FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), segments[0], StringComparison.Ordinal));
        if (root is null || !IsPlainPath(root))
        {
            return await WriteStatusAsync(output, 404, "Not Found", null, cancellationToken);
        }

        string? resolved = Resolve(root, segments[1..]);
        if (resolved is null)
        {
            return await WriteStatusAsync(output, 404, "Not Found", null, cancellationToken);
        }

        if (Directory.Exists(resolved))
        {
            return await WriteListingAsync(output, EnumerateVisible(resolved), method, cancellationToken, segments);
        }

        return await WriteFileAsync(output, resolved, method, cancellationToken);
    }

    /// <summary>Writes a status-only error response (400 or 413) for a head that could not be read.</summary>
    public static Task WriteErrorAsync(Stream output, int status, CancellationToken cancellationToken) => status switch
    {
        413 => WriteStatusAsync(output, 413, "Content Too Large", null, cancellationToken),
        _ => WriteStatusAsync(output, 400, "Bad Request", null, cancellationToken),
    };

    /// <summary>The path to <paramref name="relative"/> under <paramref name="root"/>, or null when a segment is refused.</summary>
    private static string? Resolve(string root, string[] relative)
    {
        string candidate = root;
        foreach (string segment in relative)
        {
            if (OperatingSystem.IsWindows() && (segment.Contains(':') || segment.EndsWith('.') || segment.EndsWith(' ')))
            {
                return null;
            }

            candidate = Path.Combine(candidate, segment);
            if (!IsPlainPath(candidate))
            {
                return null;
            }
        }

        string full = Path.GetFullPath(candidate);
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full == root || full.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? full : null;
    }

    /// <summary>False for a symbolic link or a reparse point, and for a path that is missing.</summary>
    private static bool IsPlainPath(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (!info.Exists)
        {
            return false;
        }

        return info.LinkTarget is null && (info.Attributes & FileAttributes.ReparsePoint) == 0;
    }

    private static IReadOnlyList<string> EnumerateVisible(string folder) =>
        new DirectoryInfo(folder).EnumerateFileSystemInfos()
            .Where(info => info.LinkTarget is null && (info.Attributes & FileAttributes.ReparsePoint) == 0)
            .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
            .Select(info => info.Name)
            .ToList();

    private bool TokenMatches(string segment)
    {
        byte[] candidate = Encoding.UTF8.GetBytes(Uri.UnescapeDataString(segment));
        return CryptographicOperations.FixedTimeEquals(candidate, _token);
    }

    private async Task<int> WriteListingAsync(Stream output, IReadOnlyList<string> names, string method, CancellationToken cancellationToken, string[]? folderSegments = null)
    {
        string prefix = "/" + Encoding.UTF8.GetString(_token) + "/";
        string basePath = folderSegments is null ? prefix : prefix + string.Join('/', folderSegments.Select(Uri.EscapeDataString)) + "/";
        StringBuilder html = new();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>File share</title></head><body><ul>");
        foreach (string name in names)
        {
            string href = basePath + Uri.EscapeDataString(name);
            html.Append("<li><a href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">")
                .Append(WebUtility.HtmlEncode(name)).Append("</a></li>");
        }

        html.Append("</ul></body></html>");
        byte[] body = Encoding.UTF8.GetBytes(html.ToString());
        await WriteHeadAsync(output, 200, "OK", "text/html; charset=utf-8", body.Length, null, cancellationToken);
        if (method == "GET")
        {
            await output.WriteAsync(body, cancellationToken);
        }

        return 200;
    }

    private async Task<int> WriteFileAsync(Stream output, string file, string method, CancellationToken cancellationToken)
    {
        if (!await ReadSlots.WaitAsync(0, cancellationToken))
        {
            return await WriteStatusAsync(output, 503, "Service Unavailable", "Retry-After: 1", cancellationToken);
        }

        try
        {
            FileInfo info = new(file);
            string name = info.Name;
            string disposition = "Content-Disposition: attachment; filename*=UTF-8''" + Uri.EscapeDataString(name);
            await WriteHeadAsync(output, 200, "OK", "application/octet-stream", info.Length, disposition, cancellationToken);
            if (method == "GET")
            {
                await using FileStream source = new(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await source.CopyToAsync(output, cancellationToken);
            }

            return 200;
        }
        finally
        {
            ReadSlots.Release();
        }
    }

    private static Task<int> WriteStatusAsync(Stream output, int status, string reason, string? extraHeader, CancellationToken cancellationToken) =>
        WriteStatusHeadAsync(output, status, reason, extraHeader, cancellationToken);

    private static async Task<int> WriteStatusHeadAsync(Stream output, int status, string reason, string? extraHeader, CancellationToken cancellationToken)
    {
        await WriteHeadAsync(output, status, reason, "text/plain; charset=utf-8", 0, extraHeader, cancellationToken);
        return status;
    }

    private static async Task WriteHeadAsync(Stream output, int status, string reason, string contentType, long length, string? extra, CancellationToken cancellationToken)
    {
        StringBuilder head = new();
        head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
        head.Append("Content-Type: ").Append(contentType).Append("\r\n");
        head.Append("Content-Length: ").Append(length).Append("\r\n");
        if (extra is not null)
        {
            head.Append(extra).Append("\r\n");
        }

        head.Append("Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        await output.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), cancellationToken);
    }
}
