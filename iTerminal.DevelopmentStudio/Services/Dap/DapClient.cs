using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace iTerminal.DevelopmentStudio.Services.Dap;

/// <summary>An event the adapter sent (<c>stopped</c>, <c>output</c>, <c>terminated</c>, ...).</summary>
public sealed record DapEvent(string Name, JsonElement Body);

/// <summary>A request the adapter refused, or a response it could not give. The message is the adapter's own text.</summary>
public sealed class DapException(string message) : Exception(message);

/// <summary>
/// A client for the Debug Adapter Protocol over a pair of streams (the adapter's standard output and input): messages are
/// framed with a <c>Content-Length</c> header. Requests are answered by sequence number; events go to a callback on the
/// reader's thread. Reverse requests from the adapter are refused, so the adapter does not wait on them.
/// </summary>
public sealed class DapClient : IAsyncDisposable
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Action<DapEvent> _onEvent;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Lock _writeLock = new();
    private int _sequence;
    private Task? _reader;

    public DapClient(Stream input, Stream output, Action<DapEvent> onEvent)
    {
        _input = input;
        _output = output;
        _onEvent = onEvent;
    }

    /// <summary>Starts reading messages in the background. The reader ends when the stream closes; pending requests then fail.</summary>
    public void Start() => _reader = Task.Run(ReadLoopAsync);

    /// <summary>Sends a request and waits for its response. Throws <see cref="DapException"/> when the adapter refuses it.</summary>
    public async Task<JsonElement> RequestAsync(string command, JsonNode? arguments, CancellationToken cancellationToken = default)
    {
        int seq = Interlocked.Increment(ref _sequence);
        var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = response;

        var message = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command,
        };
        if (arguments is not null) message["arguments"] = arguments;

        await WriteAsync(message, cancellationToken);
        using var registration = cancellationToken.Register(() => response.TrySetCanceled(cancellationToken));
        try
        {
            return await response.Task;
        }
        finally
        {
            _pending.TryRemove(seq, out _);
        }
    }

    /// <summary>Frames and writes one message. Writes are serialised, so two requests never interleave on the stream.</summary>
    private async Task WriteAsync(JsonNode message, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        var frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame, header.Length);

        // The lock is taken for the write only: a synchronous Write under a lock is what keeps frames whole.
        byte[] bytes = frame;
        await Task.Run(() =>
        {
            lock (_writeLock)
            {
                _output.Write(bytes, 0, bytes.Length);
                _output.Flush();
            }
        }, cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (true)
            {
                string? json = await ReadFrameAsync();
                if (json is null) break;
                HandleMessage(JsonDocument.Parse(json).RootElement);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or JsonException or InvalidOperationException)
        {
            // The adapter went away or sent something unreadable: the pending requests fail below.
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new DapException("The debug adapter stopped."));
            }
        }
    }

    private void HandleMessage(JsonElement message)
    {
        string type = message.GetProperty("type").GetString() ?? string.Empty;
        switch (type)
        {
            case "response":
                int requestSeq = message.GetProperty("request_seq").GetInt32();
                if (!_pending.TryRemove(requestSeq, out var source)) return;
                if (message.TryGetProperty("success", out var success) && success.GetBoolean())
                {
                    source.TrySetResult(message.TryGetProperty("body", out var body) ? body.Clone() : default);
                }
                else
                {
                    string text = message.TryGetProperty("message", out var msg) ? msg.GetString() ?? "The request failed." : "The request failed.";
                    source.TrySetException(new DapException(text));
                }

                break;
            case "event":
                string name = message.GetProperty("event").GetString() ?? string.Empty;
                JsonElement body2 = message.TryGetProperty("body", out var b) ? b.Clone() : default;
                _onEvent(new DapEvent(name, body2));
                break;
            case "request":
                // A reverse request (for example runInTerminal) is refused, so the adapter is never left waiting on it.
                int seq = Interlocked.Increment(ref _sequence);
                string command = message.TryGetProperty("command", out var c) ? c.GetString() ?? string.Empty : string.Empty;
                var refusal = new JsonObject
                {
                    ["seq"] = seq,
                    ["type"] = "response",
                    ["request_seq"] = message.GetProperty("seq").GetInt32(),
                    ["success"] = false,
                    ["command"] = command,
                    ["message"] = "Not supported by the Development Studio.",
                };
                _ = WriteAsync(refusal, CancellationToken.None);
                break;
        }
    }

    /// <summary>Reads one message body, following the <c>Content-Length</c> header. Null at end of stream.</summary>
    private async Task<string?> ReadFrameAsync()
    {
        int length = -1;
        while (true)
        {
            string? line = await ReadLineAsync();
            if (line is null) return null;
            if (line.Length == 0) break;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim());
            }
        }

        if (length < 0) return null;
        byte[] buffer = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = await _input.ReadAsync(buffer.AsMemory(read, length - read));
            if (n == 0) return null;
            read += n;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>Reads a header line up to CRLF (or LF). Null at end of stream.</summary>
    private async Task<string?> ReadLineAsync()
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            int n = await _input.ReadAsync(one.AsMemory(0, 1));
            if (n == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
            if (one[0] == '\n') break;
            if (one[0] != '\r') bytes.Add(one[0]);
        }

        return Encoding.ASCII.GetString([.. bytes]);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _input.DisposeAsync();
            await _output.DisposeAsync();
        }
        catch (IOException)
        {
            // Closing a pipe the adapter already closed is not an error.
        }

        if (_reader is not null)
        {
            await Task.WhenAny(_reader, Task.Delay(TimeSpan.FromSeconds(2)));
        }
    }
}
