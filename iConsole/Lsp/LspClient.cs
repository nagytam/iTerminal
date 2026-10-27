using System.Diagnostics;
using System.Text.Json;

namespace iConsole.Lsp;

/// <summary>A place in a document: a line and a UTF-16 character in it, both from 0 (what a .NET string index is).</summary>
public readonly record struct LspPosition(int Line, int Character);

public readonly record struct LspRange(LspPosition Start, LspPosition End);

/// <summary>How serious a diagnostic is, as the protocol numbers them.</summary>
public enum LspSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4,
}

public sealed record LspDiagnostic(LspRange Range, LspSeverity Severity, string Message, string? Source = null, string? Code = null);

public sealed record LspCompletionItem(string Label, string InsertText, string? Detail, int Kind);

public sealed record LspLocation(string Uri, LspRange Range);

/// <summary>The features a server said it has in its answer to <c>initialize</c>.</summary>
public sealed record LspCapabilities(bool Hover, bool Completion, bool Definition, string ServerName);

/// <summary>
/// A client for one language server: starts the conversation, keeps the server told of the text of open documents, collects
/// its diagnostics, and asks for hover text, completions and definitions. It talks through a <see cref="JsonRpcConnection"/>, so
/// it works over a process's pipes (<see cref="LspProcess"/>) or any other pair of streams. Text is sent whole on each change (full
/// document sync), which every server accepts. Positions are UTF-16, the protocol's default.
/// </summary>
public sealed class LspClient : IDisposable
{
    private readonly JsonRpcConnection _connection;
    private readonly TimeSpan _timeout;
    private readonly Dictionary<string, int> _versions = [];
    private readonly Lock _gate = new();
    private bool _shutDown;

    public LspClient(JsonRpcConnection connection, TimeSpan? requestTimeout = null)
    {
        _connection = connection;
        _timeout = requestTimeout ?? TimeSpan.FromSeconds(10);
        _connection.OnNotification("textDocument/publishDiagnostics", OnPublishDiagnostics);
        // Requests a server may make of its client: answer them so it is not left waiting.
        _connection.OnRequest("client/registerCapability", _ => Task.FromResult<object?>(null));
        _connection.OnRequest("client/unregisterCapability", _ => Task.FromResult<object?>(null));
        _connection.OnRequest("window/workDoneProgress/create", _ => Task.FromResult<object?>(null));
        _connection.OnRequest("workspace/configuration", parameters => Task.FromResult<object?>(
            parameters is { } p && p.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array
                ? Enumerable.Repeat<object?>(null, items.GetArrayLength()).ToArray()
                : Array.Empty<object?>()));
        _connection.OnRequest("workspace/workspaceFolders", _ => Task.FromResult<object?>(null));
    }

    /// <summary>What the server can do; null until <see cref="InitializeAsync"/> has finished.</summary>
    public LspCapabilities? Capabilities { get; private set; }

    /// <summary>True between <see cref="InitializeAsync"/> and <see cref="ShutdownAsync"/>.</summary>
    public bool IsRunning => Capabilities is not null && !_shutDown && _connection.IsOpen;

    /// <summary>Raised on a worker thread with a document's uri and its current diagnostics whenever the server publishes them.</summary>
    public event Action<string, IReadOnlyList<LspDiagnostic>>? DiagnosticsReceived;

    /// <summary>Raised when the connection to the server closes.</summary>
    public event Action? Closed
    {
        add => _connection.Closed += value;
        remove => _connection.Closed -= value;
    }

    /// <summary>The uri of a file path, as servers want it.</summary>
    public static string UriOf(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    /// <summary>Starts the connection if it is not started, introduces the client to the server and waits for its answer.</summary>
    public async Task<LspCapabilities> InitializeAsync(string? rootUri = null, CancellationToken cancellationToken = default)
    {
        if (!_connection.IsOpen)
        {
            _connection.Start();
        }

        var parameters = new Dictionary<string, object?>
        {
            ["processId"] = Environment.ProcessId,
            ["clientInfo"] = new Dictionary<string, object?> { ["name"] = "iConsole" },
            ["rootUri"] = rootUri,
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["textDocument"] = new Dictionary<string, object?>
                {
                    ["synchronization"] = new Dictionary<string, object?> { ["didSave"] = false, ["dynamicRegistration"] = false },
                    ["publishDiagnostics"] = new Dictionary<string, object?> { ["relatedInformation"] = false },
                    ["hover"] = new Dictionary<string, object?> { ["contentFormat"] = new[] { "plaintext", "markdown" } },
                    ["completion"] = new Dictionary<string, object?>
                    {
                        ["completionItem"] = new Dictionary<string, object?> { ["snippetSupport"] = false },
                    },
                    ["definition"] = new Dictionary<string, object?> { ["linkSupport"] = false },
                },
                ["general"] = new Dictionary<string, object?> { ["positionEncodings"] = new[] { "utf-16" } },
            },
        };
        JsonElement? result = await _connection.RequestAsync("initialize", parameters, _timeout, cancellationToken).ConfigureAwait(false);
        JsonElement caps = result is { } r && r.TryGetProperty("capabilities", out JsonElement c) ? c : default;
        string name = result is { } r2 && r2.TryGetProperty("serverInfo", out JsonElement info) && info.TryGetProperty("name", out JsonElement n)
            ? n.GetString() ?? string.Empty
            : string.Empty;
        Capabilities = new LspCapabilities(Provides(caps, "hoverProvider"), Provides(caps, "completionProvider"), Provides(caps, "definitionProvider"), name);
        _connection.Notify("initialized", new Dictionary<string, object?>());
        return Capabilities;
    }

    private static bool Provides(JsonElement capabilities, string name)
    {
        if (capabilities.ValueKind != JsonValueKind.Object || !capabilities.TryGetProperty(name, out JsonElement value))
        {
            return false;
        }

        return value.ValueKind is JsonValueKind.Object or JsonValueKind.True;
    }

    /// <summary>Tells the server a document is open with this text. Version 1; later changes count up.</summary>
    public void OpenDocument(string uri, string languageId, string text)
    {
        lock (_gate)
        {
            _versions[uri] = 1;
        }

        _connection.Notify("textDocument/didOpen", new Dictionary<string, object?>
        {
            ["textDocument"] = new Dictionary<string, object?> { ["uri"] = uri, ["languageId"] = languageId, ["version"] = 1, ["text"] = text },
        });
    }

    /// <summary>Sends the whole new text of an open document. Does nothing for a document that was not opened.</summary>
    public void ChangeDocument(string uri, string text)
    {
        int version;
        lock (_gate)
        {
            if (!_versions.TryGetValue(uri, out version))
            {
                return;
            }

            _versions[uri] = ++version;
        }

        _connection.Notify("textDocument/didChange", new Dictionary<string, object?>
        {
            ["textDocument"] = new Dictionary<string, object?> { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new[] { new Dictionary<string, object?> { ["text"] = text } },
        });
    }

    public void CloseDocument(string uri)
    {
        lock (_gate)
        {
            if (!_versions.Remove(uri))
            {
                return;
            }
        }

        _connection.Notify("textDocument/didClose", new Dictionary<string, object?>
        {
            ["textDocument"] = new Dictionary<string, object?> { ["uri"] = uri },
        });
    }

    private static Dictionary<string, object?> At(string uri, LspPosition position) => new()
    {
        ["textDocument"] = new Dictionary<string, object?> { ["uri"] = uri },
        ["position"] = new Dictionary<string, object?> { ["line"] = position.Line, ["character"] = position.Character },
    };

    /// <summary>The text the server shows for <paramref name="position"/> (markdown or plain), or null when it has none.</summary>
    public async Task<string?> HoverAsync(string uri, LspPosition position, CancellationToken cancellationToken = default)
    {
        JsonElement? result = await _connection.RequestAsync("textDocument/hover", At(uri, position), _timeout, cancellationToken).ConfigureAwait(false);
        if (result is not { } hover || !hover.TryGetProperty("contents", out JsonElement contents))
        {
            return null;
        }

        string text = ContentsText(contents).Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Hover contents come as a string, a <c>{language, value}</c> pair, a <c>{kind, value}</c> markup or an array of those.</summary>
    private static string ContentsText(JsonElement contents)
    {
        switch (contents.ValueKind)
        {
            case JsonValueKind.String:
                return contents.GetString() ?? string.Empty;
            case JsonValueKind.Object when contents.TryGetProperty("value", out JsonElement value):
                return value.GetString() ?? string.Empty;
            case JsonValueKind.Array:
                return string.Join("\n\n", contents.EnumerateArray().Select(ContentsText).Where(t => t.Length > 0));
            default:
                return string.Empty;
        }
    }

    /// <summary>The completions the server offers at <paramref name="position"/>, in the order it gave them.</summary>
    public async Task<IReadOnlyList<LspCompletionItem>> CompletionAsync(string uri, LspPosition position, CancellationToken cancellationToken = default)
    {
        JsonElement? result = await _connection.RequestAsync("textDocument/completion", At(uri, position), _timeout, cancellationToken).ConfigureAwait(false);
        if (result is not { } answer)
        {
            return [];
        }

        // The answer is an array, or a list object with the array in "items".
        JsonElement items = answer.ValueKind == JsonValueKind.Array ? answer
            : answer.TryGetProperty("items", out JsonElement inner) ? inner : default;
        if (items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<LspCompletionItem>();
        foreach (JsonElement item in items.EnumerateArray())
        {
            string label = item.TryGetProperty("label", out JsonElement l) ? l.GetString() ?? string.Empty : string.Empty;
            string insert = item.TryGetProperty("insertText", out JsonElement i) && i.ValueKind == JsonValueKind.String
                ? i.GetString() ?? label
                : item.TryGetProperty("textEdit", out JsonElement edit) && edit.TryGetProperty("newText", out JsonElement newText)
                    ? newText.GetString() ?? label
                    : label;
            string? detail = item.TryGetProperty("detail", out JsonElement d) ? d.GetString() : null;
            int kind = item.TryGetProperty("kind", out JsonElement k) && k.TryGetInt32(out int kindValue) ? kindValue : 0;
            if (label.Length > 0)
            {
                list.Add(new LspCompletionItem(label, insert, detail, kind));
            }
        }

        return list;
    }

    /// <summary>Where the symbol at <paramref name="position"/> is defined; empty when the server does not know.</summary>
    public async Task<IReadOnlyList<LspLocation>> DefinitionAsync(string uri, LspPosition position, CancellationToken cancellationToken = default)
    {
        JsonElement? result = await _connection.RequestAsync("textDocument/definition", At(uri, position), _timeout, cancellationToken).ConfigureAwait(false);
        if (result is not { } answer)
        {
            return [];
        }

        var locations = new List<LspLocation>();
        foreach (JsonElement item in answer.ValueKind == JsonValueKind.Array ? answer.EnumerateArray().ToList() : [answer])
        {
            // A Location has uri and range; a LocationLink has targetUri and targetSelectionRange.
            string? targetUri = item.TryGetProperty("uri", out JsonElement u) ? u.GetString()
                : item.TryGetProperty("targetUri", out JsonElement tu) ? tu.GetString() : null;
            JsonElement range = item.TryGetProperty("range", out JsonElement r) ? r
                : item.TryGetProperty("targetSelectionRange", out JsonElement tr) ? tr : default;
            if (targetUri is not null && range.ValueKind == JsonValueKind.Object)
            {
                locations.Add(new LspLocation(targetUri, ReadRange(range)));
            }
        }

        return locations;
    }

    private static LspPosition ReadPosition(JsonElement position) =>
        new(position.TryGetProperty("line", out JsonElement l) && l.TryGetInt32(out int line) ? line : 0,
            position.TryGetProperty("character", out JsonElement c) && c.TryGetInt32(out int character) ? character : 0);

    private static LspRange ReadRange(JsonElement range) =>
        new(range.TryGetProperty("start", out JsonElement s) ? ReadPosition(s) : default,
            range.TryGetProperty("end", out JsonElement e) ? ReadPosition(e) : default);

    private void OnPublishDiagnostics(JsonElement? parameters)
    {
        if (parameters is not { } p || !p.TryGetProperty("uri", out JsonElement uriElement))
        {
            return;
        }

        var diagnostics = new List<LspDiagnostic>();
        if (p.TryGetProperty("diagnostics", out JsonElement array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in array.EnumerateArray())
            {
                LspRange range = item.TryGetProperty("range", out JsonElement r) ? ReadRange(r) : default;
                int severity = item.TryGetProperty("severity", out JsonElement s) && s.TryGetInt32(out int value) ? value : 1;
                string message = item.TryGetProperty("message", out JsonElement m) ? m.GetString() ?? string.Empty : string.Empty;
                string? source = item.TryGetProperty("source", out JsonElement so) ? so.GetString() : null;
                string? code = item.TryGetProperty("code", out JsonElement co) ? co.ToString() : null;
                diagnostics.Add(new LspDiagnostic(range, Enum.IsDefined((LspSeverity)severity) ? (LspSeverity)severity : LspSeverity.Error, message, source, code));
            }
        }

        DiagnosticsReceived?.Invoke(uriElement.GetString() ?? string.Empty, diagnostics);
    }

    /// <summary>Asks the server to shut down and then exit. Safe to call twice; a server that does not answer is given up on.</summary>
    public async Task ShutdownAsync()
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;
        try
        {
            if (_connection.IsOpen)
            {
                await _connection.RequestAsync("shutdown", null, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                _connection.Notify("exit", null);
            }
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException or JsonRpcException)
        {
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}

/// <summary>A language server started as a child process and talked to over its standard input and output.</summary>
public sealed class LspProcess : IDisposable
{
    private readonly Process _process;

    private LspProcess(Process process)
    {
        _process = process;
        Connection = new JsonRpcConnection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
    }

    public JsonRpcConnection Connection { get; }

    /// <summary>Starts <paramref name="command"/> with <paramref name="arguments"/>. Returns null when it cannot be started (not installed).</summary>
    public static LspProcess? TryStart(string command, IEnumerable<string>? arguments = null, string? workingDirectory = null)
    {
        var info = new ProcessStartInfo(command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (string argument in arguments ?? [])
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            Process? process = Process.Start(info);
            if (process is null)
            {
                return null;
            }

            // A server writes its log to standard error; an unread pipe would stop it when the buffer fills.
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            return new LspProcess(process);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public bool HasExited => _process.HasExited;

    public void Dispose()
    {
        Connection.Dispose();
        try
        {
            if (!_process.HasExited && !_process.WaitForExit(1000))
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        _process.Dispose();
    }
}
