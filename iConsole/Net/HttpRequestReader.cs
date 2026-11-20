using System.Text;

namespace iConsole.Net;

/// <summary>What <see cref="HttpRequestReader"/> made of a request.</summary>
public enum HttpReadStatus
{
    /// <summary>A complete request line and headers were read.</summary>
    Ok,

    /// <summary>The head was longer than the size cap.</summary>
    TooLarge,

    /// <summary>The request line was not <c>METHOD target HTTP/1.x</c> with an origin-form target.</summary>
    Malformed,

    /// <summary>The client sent nothing in time, or closed before the blank line.</summary>
    TimedOut,
}

/// <summary>A request head: the method and the raw target. Headers are read but not kept.</summary>
public sealed record HttpRequestHead(string Method, string Target);

/// <summary>The result of reading one request head.</summary>
public sealed record HttpReadResult(HttpReadStatus Status, HttpRequestHead? Request);

/// <summary>
/// Reads one HTTP/1.1 request head (request line and headers, up to the blank line) from a stream. The head is
/// capped at <see cref="MaxBytes"/>, and the read is cut off after <see cref="ReadTimeout"/>, so a silent client
/// cannot hold a connection. The body is never read: the callers serve GET and HEAD only.
/// </summary>
public sealed class HttpRequestReader(int maxBytes = 8192, TimeSpan? readTimeout = null)
{
    private static readonly byte[] BlankLine = "\r\n\r\n"u8.ToArray();

    public int MaxBytes { get; } = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));

    public TimeSpan ReadTimeout { get; } = readTimeout ?? TimeSpan.FromSeconds(10);

    public async Task<HttpReadResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        List<byte> head = [];
        byte[] one = new byte[1];
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(one.AsMemory(0, 1), timeout.Token);
                if (read == 0)
                {
                    return new HttpReadResult(HttpReadStatus.TimedOut, null);
                }

                head.Add(one[0]);
                if (head.Count > MaxBytes)
                {
                    return new HttpReadResult(HttpReadStatus.TooLarge, null);
                }

                if (head.Count >= BlankLine.Length && EndsWithBlankLine(head))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HttpReadResult(HttpReadStatus.TimedOut, null);
        }
        catch (IOException)
        {
            return new HttpReadResult(HttpReadStatus.TimedOut, null);
        }

        return Parse(Encoding.ASCII.GetString(head.ToArray()));
    }

    /// <summary>Parses a request head (the text up to and including the blank line).</summary>
    public static HttpReadResult Parse(string head)
    {
        int lineEnd = head.IndexOf("\r\n", StringComparison.Ordinal);
        string requestLine = lineEnd < 0 ? head : head[..lineEnd];
        string[] parts = requestLine.Split(' ');
        if (parts.Length != 3 || parts[0].Length == 0 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
        {
            return new HttpReadResult(HttpReadStatus.Malformed, null);
        }

        if (!parts[1].StartsWith('/'))
        {
            return new HttpReadResult(HttpReadStatus.Malformed, null);
        }

        return new HttpReadResult(HttpReadStatus.Ok, new HttpRequestHead(parts[0], parts[1]));
    }

    private static bool EndsWithBlankLine(List<byte> head)
    {
        int start = head.Count - BlankLine.Length;
        for (int i = 0; i < BlankLine.Length; i++)
        {
            if (head[start + i] != BlankLine[i])
            {
                return false;
            }
        }

        return true;
    }
}
