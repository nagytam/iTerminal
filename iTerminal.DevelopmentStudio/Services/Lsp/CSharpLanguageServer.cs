using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Model;

namespace iTerminal.DevelopmentStudio.Services.Lsp;

/// <summary>
/// The C# language server (<c>csharp-ls</c>) for the Development studio. A session belongs to one folder: the first <c>.cs</c>
/// file opened in a folder starts the server, a file opened in another folder stops it and starts a new one, and the
/// diagnostics of the old session are dropped. Only files opened in the studio get diagnostics, and a file stays open to the
/// server until the folder changes or the app exits. The text the server holds is the text read when the file was opened, so
/// after an edit on disk its marks are stale until the file is opened again.
/// </summary>
/// <remarks>
/// <see cref="OpenFile"/> never waits on the server: the process start and the shutdown run on background tasks, which change
/// <see cref="Version"/>. A listener reads the state and the diagnostics on its own UI pass.
/// </remarks>
public sealed class CSharpLanguageServer : IAsyncDisposable
{
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InitializeTimeout = TimeSpan.FromMinutes(2);

    private readonly ILanguageServerLauncher _launcher;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<LiveDiagnostic>> _diagnostics = new(StringComparer.OrdinalIgnoreCase);
    private Session? _session;

    /// <summary>
    /// The shutdown bound is an optional parameter, so a test with a silent server does not wait five seconds. The container
    /// uses the default.
    /// </summary>
    public CSharpLanguageServer(ILanguageServerLauncher launcher, TimeSpan? shutdownTimeout = null)
    {
        _launcher = launcher;
        _shutdownTimeout = shutdownTimeout ?? DefaultShutdownTimeout;
    }

    private long _version;

    /// <summary>
    /// Counts the changes to the state and the diagnostics. A listener (the dialog) compares it on its idle pass instead of
    /// subscribing, so a transient dialog that closes is not kept alive by the singleton.
    /// </summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>The folder of the current session, or null when there is none.</summary>
    public string? Folder
    {
        get
        {
            lock (_lock) return _session?.Folder;
        }
    }

    /// <summary>The state of the current session.</summary>
    public LanguageServerState State
    {
        get
        {
            lock (_lock) return _session?.State ?? LanguageServerState.Stopped;
        }
    }

    /// <summary>
    /// Tells the server that <paramref name="path"/> is open in the studio, in <paramref name="folder"/>. A folder other than the
    /// current one starts a new session and stops the old one in the background. Returns at once.
    /// </summary>
    public void OpenFile(string folder, string path, string text)
    {
        Session? stale = null;
        Session? started = null;
        Session? ready = null;
        lock (_lock)
        {
            if (_session is null || !string.Equals(_session.Folder, folder, StringComparison.OrdinalIgnoreCase))
            {
                stale = _session;
                _session = new Session(folder);
                _diagnostics.Clear();
                started = _session;
            }

            // Ready is read under the same lock StartAsync takes to set it and to take the list of files to replay, so
            // each file is sent exactly once: either here, or in the replay.
            _session.Opened[path] = text;
            if (started is null && _session.State == LanguageServerState.Ready) ready = _session;
        }

        if (stale is not null) _ = StopAsync(stale);
        if (started is not null) _ = StartAsync(started);
        if (ready?.Client is { } client) _ = DidOpenAsync(client, path, text);
        Touch();
    }

    /// <summary>The live diagnostics of one open file, in the order the server reported them.</summary>
    public IReadOnlyList<LiveDiagnostic> DiagnosticsFor(string path)
    {
        lock (_lock)
        {
            return _diagnostics.TryGetValue(path, out List<LiveDiagnostic>? list) ? [.. list] : [];
        }
    }

    /// <summary>The live diagnostics of every open file, file by file.</summary>
    public IReadOnlyList<LiveDiagnostic> AllDiagnostics()
    {
        lock (_lock)
        {
            return [.. _diagnostics.Values.SelectMany(list => list)];
        }
    }

    /// <summary>Stops the current session: a bounded shutdown, then the process is killed.</summary>
    public async ValueTask DisposeAsync()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
            _session = null;
            _diagnostics.Clear();
        }

        if (session is not null) await StopAsync(session);
    }

    private async Task StartAsync(Session session)
    {
        try
        {
            string? program = _launcher.Locate();
            if (program is null)
            {
                SetState(session, LanguageServerState.Missing);
                return;
            }

            ILanguageServerProcess? process = _launcher.Launch(program, session.Folder);
            if (process is null)
            {
                SetState(session, LanguageServerState.Missing);
                return;
            }

            var client = new LspClient(process.Input, process.Output, (uri, diagnostics) => OnDiagnostics(session, uri, diagnostics));
            bool stillCurrent;
            lock (_lock)
            {
                stillCurrent = !session.Stopped;
                session.Process = process;
                session.Client = client;
            }

            if (!stillCurrent)
            {
                // The session was stopped while the process was starting: StopAsync found no process to kill, so kill it here.
                process.Kill();
                await client.DisposeAsync();
                return;
            }

            client.Start();
            using var cancellation = new CancellationTokenSource(InitializeTimeout);
            await client.RequestAsync("initialize", InitializeParameters(session.Folder), cancellation.Token);
            await client.NotifyAsync("initialized", new JsonObject(), cancellation.Token);

            List<KeyValuePair<string, string>> toOpen;
            lock (_lock)
            {
                if (session.Stopped) return;
                session.State = LanguageServerState.Ready;
                toOpen = [.. session.Opened];
            }

            // Files opened while the server was starting, including the one that started it, are sent now.
            foreach (KeyValuePair<string, string> file in toOpen)
            {
                await DidOpenAsync(client, file.Key, file.Value);
            }

            Touch();
        }
        catch (Exception exception) when (exception is LspException or IOException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The server could not start or did not answer: the session ends, and the editor keeps working without marks.
            await StopAsync(session);
            SetState(session, LanguageServerState.Stopped);
        }
    }

    private async Task StopAsync(Session session)
    {
        LspClient? client;
        ILanguageServerProcess? process;
        lock (_lock)
        {
            session.Stopped = true;
            client = session.Client;
            process = session.Process;
        }

        try
        {
            if (client is not null)
            {
                using var cancellation = new CancellationTokenSource(_shutdownTimeout);
                await client.RequestAsync("shutdown", null, cancellation.Token);
                await client.NotifyAsync("exit", null, cancellation.Token);
            }
        }
        catch (Exception exception) when (exception is LspException or IOException or OperationCanceledException)
        {
            // A server that does not answer within the bound is killed below.
        }

        process?.Kill();
        if (client is not null) await client.DisposeAsync();
    }

    private async Task DidOpenAsync(LspClient client, string path, string text)
    {
        try
        {
            await client.NotifyAsync("textDocument/didOpen", new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["uri"] = ToUri(path),
                    ["languageId"] = "csharp",
                    ["version"] = 1,
                    ["text"] = text,
                },
            }, CancellationToken.None);
        }
        catch (Exception exception) when (exception is LspException or IOException or ObjectDisposedException)
        {
            // The server went away: its diagnostics stop with it.
        }
    }

    private void OnDiagnostics(Session session, string uri, JsonArray diagnostics)
    {
        string path;
        var live = new List<LiveDiagnostic>();
        try
        {
            path = new Uri(uri).LocalPath;
            foreach (JsonNode? node in diagnostics)
            {
                if (node is not JsonObject diagnostic) continue;
                int severity = diagnostic["severity"]?.GetValue<int>() ?? 1;
                if (severity is not (1 or 2)) continue;   // Only errors and warnings reach the Error List.

                JsonObject? start = (diagnostic["range"] as JsonObject)?["start"] as JsonObject;
                int line = start?["line"]?.GetValue<int>() ?? 0;
                int character = start?["character"]?.GetValue<int>() ?? 0;
                string code = diagnostic["code"]?.ToString() ?? string.Empty;
                string message = diagnostic["message"]?.GetValue<string>() ?? string.Empty;
                live.Add(new LiveDiagnostic(path, line + 1, character + 1, severity == 1, code, message));
            }
        }
        catch (Exception exception) when (exception is UriFormatException or InvalidOperationException or FormatException)
        {
            return;
        }

        lock (_lock)
        {
            // A session that was replaced, or a file that is no longer open to it, gets no diagnostics.
            if (session != _session || !session.Opened.ContainsKey(path)) return;
            _diagnostics[path] = live;
        }

        Touch();
    }

    private void Touch() => Interlocked.Increment(ref _version);

    private void SetState(Session session, LanguageServerState state)
    {
        lock (_lock)
        {
            if (session == _session) session.State = state;
        }

        Touch();
    }

    private static JsonObject InitializeParameters(string folder) => new()
    {
        ["processId"] = Environment.ProcessId,
        ["rootUri"] = ToUri(folder),
        ["workspaceFolders"] = new JsonArray(new JsonObject
        {
            ["uri"] = ToUri(folder),
            ["name"] = Path.GetFileName(folder),
        }),
        ["capabilities"] = new JsonObject
        {
            ["textDocument"] = new JsonObject { ["publishDiagnostics"] = new JsonObject() },
        },
    };

    private static string ToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    /// <summary>One folder's server. Its fields are read and written under <see cref="_lock"/>.</summary>
    private sealed class Session(string folder)
    {
        public string Folder { get; } = folder;
        public Dictionary<string, string> Opened { get; } = new(StringComparer.OrdinalIgnoreCase);
        public LanguageServerState State { get; set; } = LanguageServerState.Starting;
        public bool Stopped { get; set; }
        public LspClient? Client { get; set; }
        public ILanguageServerProcess? Process { get; set; }
    }
}
