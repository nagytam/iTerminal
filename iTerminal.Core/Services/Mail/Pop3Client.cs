using System.Net.Security;
using System.Net.Sockets;
using System.Text;

namespace iTerminal.Core.Services.Mail;

/// <summary>A POP3 server refused a command, or the reply was not one the protocol allows.</summary>
public sealed class Pop3Exception(string message) : Exception(message);

/// <summary>
/// A POP3 session (RFC 1939, with TOP from RFC 2449), over plain TCP or implicit TLS. One session is one
/// connection: it logs in, does its work and <see cref="QuitAsync"/>s, because POP3 applies deletions only when
/// the session ends with QUIT. Cancelling the token closes the connection, which is what unblocks a read.
/// </summary>
public sealed class Pop3Client : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly CancellationTokenRegistration _cancellation;

    private Pop3Client(TcpClient tcp, Stream stream, CancellationToken cancellationToken)
    {
        _tcp = tcp;
        _stream = stream;
        _reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        _cancellation = cancellationToken.Register(() => _tcp.Dispose());
    }

    public static async Task<Pop3Client> ConnectAsync(string host, int port, bool implicitTls, CancellationToken cancellationToken = default)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, cancellationToken);
            Stream stream = tcp.GetStream();
            if (implicitTls)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsClientAsync(host);
                stream = ssl;
            }

            var client = new Pop3Client(tcp, stream, cancellationToken);
            var greeting = await client.ReadLineAsync();
            if (!greeting.StartsWith("+OK", StringComparison.Ordinal))
            {
                throw new Pop3Exception(greeting);
            }

            return client;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public async Task LoginAsync(string userName, string password)
    {
        await CommandAsync("USER " + userName);
        await CommandAsync("PASS " + password);
    }

    /// <summary>Message numbers and sizes, as the server lists them (LIST).</summary>
    public async Task<IReadOnlyList<(int Number, long Size)>> ListAsync()
    {
        await CommandAsync("LIST");
        var lines = await ReadMultilineAsync();
        var result = new List<(int, long)>(lines.Count);
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var number) && long.TryParse(parts[1], out var size))
            {
                result.Add((number, size));
            }
        }

        return result;
    }

    /// <summary>The headers of a message and nothing of its body (TOP n 0). Not every server offers TOP.</summary>
    public async Task<string> TopAsync(int number)
    {
        await CommandAsync($"TOP {number} 0");
        return string.Join("\r\n", await ReadMultilineAsync());
    }

    /// <summary>The whole message as sent, CRLF line endings (RETR).</summary>
    public async Task<string> RetrieveAsync(int number)
    {
        await CommandAsync($"RETR {number}");
        return string.Join("\r\n", await ReadMultilineAsync());
    }

    public Task DeleteAsync(int number) => CommandAsync($"DELE {number}");

    /// <summary>Ends the session. The server makes deletions final only here.</summary>
    public async Task QuitAsync()
    {
        await CommandAsync("QUIT");
    }

    private async Task<string> CommandAsync(string command)
    {
        var bytes = Encoding.UTF8.GetBytes(command + "\r\n");
        await _stream.WriteAsync(bytes);
        await _stream.FlushAsync();

        var reply = await ReadLineAsync();
        if (!reply.StartsWith("+OK", StringComparison.Ordinal))
        {
            throw new Pop3Exception(reply.StartsWith("-ERR", StringComparison.Ordinal) ? reply[4..].TrimStart() : reply);
        }

        return reply.Length > 3 ? reply[3..].TrimStart() : string.Empty;
    }

    /// <summary>Lines up to the terminating "." line, with the dot-stuffing removed.</summary>
    private async Task<List<string>> ReadMultilineAsync()
    {
        var lines = new List<string>();
        while (true)
        {
            var line = await ReadLineAsync();
            if (line == ".")
            {
                return lines;
            }

            lines.Add(line.StartsWith("..", StringComparison.Ordinal) ? line[1..] : line);
        }
    }

    private async Task<string> ReadLineAsync()
    {
        var line = await _reader.ReadLineAsync();
        return line ?? throw new Pop3Exception("The server closed the connection.");
    }

    public ValueTask DisposeAsync()
    {
        _cancellation.Dispose();
        _reader.Dispose();
        _stream.Dispose();
        _tcp.Dispose();
        return ValueTask.CompletedTask;
    }
}
