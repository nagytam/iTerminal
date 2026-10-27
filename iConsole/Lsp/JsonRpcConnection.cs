using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace iConsole.Lsp;

/// <summary>An error answer from the other side of a <see cref="JsonRpcConnection"/>.</summary>
public sealed class JsonRpcException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

/// <summary>
/// JSON-RPC 2.0 over a pair of streams with the <c>Content-Length</c> framing the Language Server Protocol uses. Requests are
/// matched to their answers by id, notifications and requests from the other side go to handlers, and the read loop runs on a
/// task of its own. It knows nothing of the protocol above it; <see cref="LspClient"/> is that. Either end of a connection can be
/// one, which is how the tests stand in for a server.
/// </summary>
public sealed class JsonRpcConnection : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Lock _writeGate = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement?>> _pending = new();
    private readonly ConcurrentDictionary<string, Action<JsonElement?>> _notificationHandlers = new();
    private readonly ConcurrentDictionary<string, Func<JsonElement?, Task<object?>>> _requestHandlers = new();
    private readonly CancellationTokenSource _stop = new();
    private long _nextId;
    private Task _reader = Task.CompletedTask;

    /// <param name="input">What the other side sends.</param>
    /// <param name="output">What this side sends.</param>
    public JsonRpcConnection(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    /// <summary>True from <see cref="Start"/> until the other side closes the stream or the connection is disposed.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Raised once when the read loop ends, whatever the reason.</summary>
    public event Action? Closed;

    /// <summary>Starts reading. Handlers should be registered first, so no message is missed.</summary>
    public void Start()
    {
        IsOpen = true;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>Runs <paramref name="handler"/> on a worker thread for each notification called <paramref name="method"/>.</summary>
    public void OnNotification(string method, Action<JsonElement?> handler) => _notificationHandlers[method] = handler;

    /// <summary>Answers each request called <paramref name="method"/> with what <paramref name="handler"/> returns (null becomes a JSON null).</summary>
    public void OnRequest(string method, Func<JsonElement?, Task<object?>> handler) => _requestHandlers[method] = handler;

    /// <summary>Sends a request and waits for its answer: the <c>result</c> element, or null for a JSON null. An error answer throws <see cref="JsonRpcException"/>.</summary>
    public async Task<JsonElement?> RequestAsync(string method, object? parameters, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        long id = Interlocked.Increment(ref _nextId);
        var source = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = source;
        try
        {
            Send(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
            return await source.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(30), linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Sends a notification, which has no answer.</summary>
    public void Notify(string method, object? parameters)
    {
        Send(new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = parameters });
    }

    private void Send(object message)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeGate)
        {
            _output.Write(header);
            _output.Write(body);
            _output.Flush();
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                byte[]? body = await ReadMessageAsync().ConfigureAwait(false);
                if (body is null)
                {
                    break;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(body);
                    Dispatch(document.RootElement.Clone());
                }
                catch (JsonException)
                {
                    // A message that is not JSON is skipped; the stream is still in step because the length framed it.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            IsOpen = false;
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new IOException("The connection closed"));
            }

            Closed?.Invoke();
        }
    }

    /// <summary>Reads the headers up to the blank line and then that many bytes. Null at the end of the stream.</summary>
    private async Task<byte[]?> ReadMessageAsync()
    {
        int length = -1;
        while (true)
        {
            string? line = await ReadHeaderLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            int colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(colon + 1)..].Trim(), out int parsed))
            {
                length = parsed;
            }
        }

        if (length < 0 || length > 64 * 1024 * 1024)
        {
            return length < 0 ? [] : null;
        }

        byte[] body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = await _input.ReadAsync(body.AsMemory(read, length - read), _stop.Token).ConfigureAwait(false);
            if (n == 0)
            {
                return null;
            }

            read += n;
        }

        return body;
    }

    private async Task<string?> ReadHeaderLineAsync()
    {
        var bytes = new List<byte>();
        byte[] one = new byte[1];
        while (true)
        {
            int n = await _input.ReadAsync(one.AsMemory(0, 1), _stop.Token).ConfigureAwait(false);
            if (n == 0)
            {
                return bytes.Count == 0 ? null : Encoding.ASCII.GetString([.. bytes]);
            }

            if (one[0] == '\n')
            {
                return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            }

            bytes.Add(one[0]);
        }
    }

    private readonly Lock _orderGate = new();
    private Task _ordered = Task.CompletedTask;

    /// <summary>Runs <paramref name="action"/> on a worker thread after every earlier one has finished; an exception does not stop the ones after it.</summary>
    private void RunInOrder(Action action)
    {
        lock (_orderGate)
        {
            _ordered = _ordered.ContinueWith(
                _ =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception)
                    {
                        // A handler that throws must not stop the connection.
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    private void Dispatch(JsonElement message)
    {
        bool hasMethod = message.TryGetProperty("method", out JsonElement methodElement);
        bool hasId = message.TryGetProperty("id", out JsonElement idElement) && idElement.ValueKind != JsonValueKind.Null;
        JsonElement? parameters = message.TryGetProperty("params", out JsonElement p) ? p : null;
        if (hasMethod)
        {
            string method = methodElement.GetString() ?? string.Empty;
            // Requests and notifications are handled in the order they arrived: a didChange must be applied before the hover
            // that follows it, and two diagnostics notifications must not swap. A request is only started in turn (its
            // answer may take as long as it likes); a notification is run to its end before the next message starts.
            if (hasId)
            {
                JsonElement requestId = idElement.Clone();
                RunInOrder(() => _ = AnswerRequestAsync(requestId, method, parameters));
            }
            else if (_notificationHandlers.TryGetValue(method, out Action<JsonElement?>? handler))
            {
                RunInOrder(() => handler(parameters));
            }

            return;
        }

        if (hasId && idElement.TryGetInt64(out long id) && _pending.TryGetValue(id, out TaskCompletionSource<JsonElement?>? source))
        {
            if (message.TryGetProperty("error", out JsonElement error))
            {
                int code = error.TryGetProperty("code", out JsonElement c) && c.TryGetInt32(out int value) ? value : 0;
                string text = error.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty;
                source.TrySetException(new JsonRpcException(code, text));
            }
            else if (message.TryGetProperty("result", out JsonElement result) && result.ValueKind != JsonValueKind.Null)
            {
                source.TrySetResult(result);
            }
            else
            {
                source.TrySetResult(null);
            }
        }
    }

    private async Task AnswerRequestAsync(JsonElement id, string method, JsonElement? parameters)
    {
        object? result = null;
        object? error = null;
        if (_requestHandlers.TryGetValue(method, out Func<JsonElement?, Task<object?>>? handler))
        {
            try
            {
                result = await handler(parameters).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                error = new Dictionary<string, object?> { ["code"] = -32603, ["message"] = exception.Message };
            }
        }
        else
        {
            error = new Dictionary<string, object?> { ["code"] = -32601, ["message"] = "Method not found: " + method };
        }

        var answer = new Dictionary<string, object?> { ["jsonrpc"] = "2.0", ["id"] = id };
        if (error is not null)
        {
            answer["error"] = error;
        }
        else
        {
            answer["result"] = result;
        }

        try
        {
            Send(answer);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        IsOpen = false;
        try
        {
            _input.Dispose();
            _output.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }
}
