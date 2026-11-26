using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Services.Lsp;

namespace iTerminal.DevelopmentStudio.Test.Services.Lsp;

/// <summary>
/// A language server the test plays over anonymous pipes, in the Content-Length framing. It answers <c>initialize</c> and
/// <c>shutdown</c> (or stays silent on shutdown when <see cref="RespondToShutdown"/> is false), and on <c>didOpen</c> it publishes
/// the diagnostics <see cref="Diagnostics"/> gives for the file, plus those for <see cref="AlsoPublishFor"/>, a file that is not open.
/// It records the methods it received, in order, and whether the client killed it.
/// </summary>
public sealed class FakeLanguageServer : ILanguageServerLauncher
{
    private readonly Lock _lock = new();
    private readonly List<string> _methods = [];
    private int _launches;
    private bool _killed;

    /// <summary>The program <see cref="Locate"/> reports; null means the server is not installed.</summary>
    public string? Program { get; set; } = "/fake/csharp-ls";

    public bool RespondToShutdown { get; set; } = true;

    /// <summary>Gives the diagnostics for a file, from its uri and text. Null publishes none.</summary>
    public Func<string, string, JsonArray>? Diagnostics { get; set; }

    /// <summary>A uri that is published on every open, although the client never opened it.</summary>
    public string? AlsoPublishFor { get; set; }

    public int Launches
    {
        get
        {
            lock (_lock) return _launches;
        }
    }

    public bool Killed
    {
        get
        {
            lock (_lock) return _killed;
        }
    }

    public IReadOnlyList<string> Methods
    {
        get
        {
            lock (_lock) return [.. _methods];
        }
    }

    public string? Locate() => Program;

    public ILanguageServerProcess? Launch(string fileName, string workingDirectory)
    {
        lock (_lock) _launches++;
        return new Playing(this);
    }

    /// <summary>The diagnostic a test publishes: an error or warning at a 0-based line and character.</summary>
    public static JsonObject Diagnostic(int line, int character, int severity, string code, string message) => new()
    {
        ["range"] = new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = line, ["character"] = character },
            ["end"] = new JsonObject { ["line"] = line, ["character"] = character + 1 },
        },
        ["severity"] = severity,
        ["code"] = code,
        ["message"] = message,
    };

    private void Record(string? method)
    {
        if (method is null) return;
        lock (_lock) _methods.Add(method);
    }

    private void MarkKilled()
    {
        lock (_lock) _killed = true;
    }

    private sealed class Playing : ILanguageServerProcess
    {
        private readonly FakeLanguageServer _owner;
        private readonly AnonymousPipeServerStream _serverOut = new(PipeDirection.Out);
        private readonly AnonymousPipeServerStream _serverIn = new(PipeDirection.In);
        private readonly AnonymousPipeClientStream _clientReads;
        private readonly AnonymousPipeClientStream _clientWrites;

        public Playing(FakeLanguageServer owner)
        {
            _owner = owner;
            _clientReads = new AnonymousPipeClientStream(PipeDirection.In, _serverOut.ClientSafePipeHandle);
            _clientWrites = new AnonymousPipeClientStream(PipeDirection.Out, _serverIn.ClientSafePipeHandle);
            _ = Task.Run(Serve);
        }

        public Stream Input => _clientWrites;
        public Stream Output => _clientReads;

        public void Kill()
        {
            // Closing the client's write end ends the fake's read without disposing a pipe that another thread is reading.
            _owner.MarkKilled();
            _clientWrites.Dispose();
        }

        private void Serve()
        {
            try
            {
                while (ReadMessage() is { } message)
                {
                    string? method = message["method"]?.GetValue<string>();
                    _owner.Record(method);
                    JsonNode? id = message["id"]?.DeepClone();
                    switch (method)
                    {
                        case "initialize":
                            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = new JsonObject { ["capabilities"] = new JsonObject() } });
                            break;
                        case "shutdown":
                            if (_owner.RespondToShutdown) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = null });
                            break;
                        case "exit":
                            return;
                        case "textDocument/didOpen":
                            PublishFor(message);
                            break;
                    }
                }
            }
            catch (IOException)
            {
                // The client killed the process: the pipes closed.
            }
            catch (ObjectDisposedException)
            {
                // The process was killed while it was reading.
            }
            finally
            {
                _serverIn.Dispose();
                _serverOut.Dispose();
            }
        }

        private void PublishFor(JsonObject message)
        {
            var document = (JsonObject)message["params"]!["textDocument"]!;
            string uri = document["uri"]!.GetValue<string>();
            string text = document["text"]!.GetValue<string>();
            Publish(uri, _owner.Diagnostics?.Invoke(uri, text) ?? []);
            if (_owner.AlsoPublishFor is { } other)
            {
                Publish(other, _owner.Diagnostics?.Invoke(other, text) ?? []);
            }
        }

        private void Publish(string uri, JsonArray diagnostics) => Send(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = diagnostics },
        });

        private void Send(JsonObject message)
        {
            byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
            byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            _serverOut.Write(header);
            _serverOut.Write(body);
            _serverOut.Flush();
        }

        private JsonObject? ReadMessage()
        {
            int length = -1;
            while (true)
            {
                string line = ReadLine();
                if (line.Length == 0) break;
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line["Content-Length:".Length..].Trim());
            }

            if (length < 0) return null;
            var buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int count = _serverIn.Read(buffer, read, length - read);
                if (count == 0) return null;
                read += count;
            }

            return JsonNode.Parse(buffer) as JsonObject;
        }

        private string ReadLine()
        {
            var bytes = new List<byte>();
            int b;
            while ((b = _serverIn.ReadByte()) != -1 && b != '\n')
            {
                if (b != '\r') bytes.Add((byte)b);
            }

            return Encoding.ASCII.GetString([.. bytes]);
        }
    }
}
