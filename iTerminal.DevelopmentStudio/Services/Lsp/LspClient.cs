using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace iTerminal.DevelopmentStudio.Services.Lsp;

/// <summary>A request the server refused, or a response it could not give. The message is the server's own text.</summary>
internal sealed class LspException(string message) : Exception(message);

/// <summary>
/// A client for the Language Server Protocol over a pair of streams: each message is framed with a <c>Content-Length</c>
/// header, as in the Debug Adapter Protocol. Responses are matched by id. Notifications from the server are passed on, and
/// <c>publishDiagnostics</c> goes to <c>onDiagnostics</c> on the reader's thread. Requests from the server (such as work
/// done progress) are answered with a null result so the server never waits on the client.
/// </summary>
internal sealed class LspClient : IAsyncDisposable
{
    private const string Header = "Content-Length:";

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Action<string, JsonArray> _onDiagnostics;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _nextId;
    private Task? _reader;

    public LspClient(Stream input, Stream output, Action<string, JsonArray> onDiagnostics)
    {
        _input = input;
        _output = output;
        _onDiagnostics = onDiagnostics;
    }

    /// <summary>Starts reading messages in the background. The reader ends when the stream closes; pending requests then fail.</summary>
    public void Start() => _reader = Task.Run(ReadLoopAsync);

    /// <summary>Sends a request and waits for its result. Throws <see cref="LspException"/> when the server refuses it.</summary>
    public async Task<JsonNode?> RequestAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        int id = Interlocked.Increment(ref _nextId);
        var response = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = response;
        try
        {
            await SendAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters?.DeepClone(),
            }, cancellationToken);
            using var registration = cancellationToken.Register(() => response.TrySetCanceled(cancellationToken));
            return await response.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Sends a notification, which has no response.</summary>
    public Task NotifyAsync(string method, JsonNode? parameters, CancellationToken cancellationToken) =>
        SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters?.DeepClone(),
        }, cancellationToken);

    private async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        byte[] header = Encoding.ASCII.GetBytes($"{Header} {body.Length.ToString(CultureInfo.InvariantCulture)}\r\n\r\n");

        // One writer at a time, so a header and its body are never split by another send.
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await _input.WriteAsync(header, cancellationToken);
            await _input.WriteAsync(body, cancellationToken);
            await _input.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await ReadMessageAsync() is { } message)
            {
                Dispatch(message);
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or FormatException or System.Text.Json.JsonException)
        {
            // The stream closed or the server wrote something that is not a message: the pending requests fail below.
        }
        finally
        {
            foreach (TaskCompletionSource<JsonNode?> pending in _pending.Values)
            {
                pending.TrySetException(new LspException("The language server stopped."));
            }
        }
    }

    /// <summary>Reads one framed message. Null at the end of the stream.</summary>
    private async Task<JsonObject?> ReadMessageAsync()
    {
        int length = -1;
        while (true)
        {
            string? line = await ReadHeaderLineAsync();
            if (line is null) return null;
            if (line.Length == 0) break;
            if (line.StartsWith(Header, StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line[Header.Length..].Trim(), CultureInfo.InvariantCulture);
            }
        }

        if (length < 0) throw new FormatException("A message has no Content-Length header.");
        byte[] body = new byte[length];
        await _output.ReadExactlyAsync(body);
        return JsonNode.Parse(body) as JsonObject;
    }

    /// <summary>Reads one header line, byte by byte, so no part of the body is read ahead. Null at the end of the stream.</summary>
    private async Task<string?> ReadHeaderLineAsync()
    {
        var bytes = new List<byte>();
        byte[] one = new byte[1];
        while (true)
        {
            int read = await _output.ReadAsync(one);
            if (read == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
            if (one[0] == '\n')
            {
                if (bytes.Count > 0 && bytes[^1] == '\r') bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString([.. bytes]);
            }

            bytes.Add(one[0]);
        }
    }

    private void Dispatch(JsonObject message)
    {
        string? method = message["method"]?.GetValue<string>();
        if (method is not null && message["id"] is not null)
        {
            // A request from the server: answered with a null result, so the server does not wait on the client.
            _ = SendAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = message["id"]!.DeepClone(), ["result"] = null }, CancellationToken.None)
                .ContinueWith(_ => { }, TaskScheduler.Default);
            return;
        }

        if (method is null)
        {
            if (message["id"] is null || !int.TryParse(message["id"]!.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return;
            if (!_pending.TryRemove(id, out TaskCompletionSource<JsonNode?>? response)) return;
            if (message["error"] is JsonObject error)
            {
                response.TrySetException(new LspException(error["message"]?.GetValue<string>() ?? "The request failed."));
            }
            else
            {
                response.TrySetResult(message["result"]?.DeepClone());
            }

            return;
        }

        if (method == "textDocument/publishDiagnostics" && message["params"] is JsonObject parameters)
        {
            string? uri = parameters["uri"]?.GetValue<string>();
            if (uri is not null && parameters["diagnostics"] is JsonArray diagnostics)
            {
                _onDiagnostics(uri, diagnostics);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (TaskCompletionSource<JsonNode?> pending in _pending.Values)
        {
            pending.TrySetException(new LspException("The client was disposed."));
        }

        _writeGate.Dispose();
        return ValueTask.CompletedTask;
    }
}
