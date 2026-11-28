using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Services.Dap;

/// <summary>What a debug session tells the Development Studio: it stopped, it resumed, the program printed, or it ended.</summary>
public interface IDebugNotification
{
}

public sealed record DebugStoppedNotification(DebugStop Stop) : IDebugNotification;

public sealed record DebugContinuedNotification(int ThreadId) : IDebugNotification;

public sealed record DebugOutputNotification(string Category, string Text) : IDebugNotification;

public sealed record DebugEndedNotification(string Reason) : IDebugNotification;

/// <summary>
/// One debugging run: an adapter process (debugpy or netcoredbg) talking DAP over its standard streams, with the launch
/// handshake, breakpoints, stepping, the call stack, variables and evaluation. Notifications arrive on the reader thread;
/// the caller marshals them to the UI.
/// </summary>
public sealed class DebugSession : IAsyncDisposable
{
    /// <summary>How long the adapter may take to say it is ready for breakpoints.</summary>
    public static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(20);

    private readonly Action<IDebugNotification> _notify;
    private readonly IDebugAdapterProcess _adapter;
    private readonly DapClient _client;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<string> _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile int _threadId = 1;
    private int _endedFlag;

    private DebugSession(IDebugAdapterProcess adapter, Action<IDebugNotification> notify)
    {
        _adapter = adapter;
        _notify = notify;
        _client = new DapClient(adapter.StandardOutput, adapter.StandardInput, OnEvent);
        adapter.Exited += (_, _) => MarkEnded($"The debugger exited with code {adapter.ExitCode}.");
        adapter.ErrorLine += (_, line) => notify(new DebugOutputNotification("stderr", line + "\n"));
    }

    /// <summary>The thread the program stopped on last; stepping and the stack use it.</summary>
    public int ThreadId => _threadId;

    /// <summary>
    /// Starts a session on an adapter process that is already running. <see cref="Start"/> is the production path; tests attach
    /// a fake adapter here instead of launching a debugger.
    /// </summary>
    public static DebugSession Attach(IDebugAdapterProcess adapter, Action<IDebugNotification> notify)
    {
        var session = new DebugSession(adapter, notify);
        session._client.Start();
        adapter.BeginReading();
        return session;
    }

    /// <summary>Starts the adapter process. Throws <see cref="InvalidOperationException"/> when it cannot be started.</summary>
    public static DebugSession Start(DebugPlan plan, string workingDirectory, Action<IDebugNotification> notify)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.FileName,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in plan.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start the debugger {plan.FileName}.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"Could not start the debugger {plan.FileName}: {exception.Message}", exception);
        }

        return Attach(new SystemDebugAdapterProcess(process), notify);
    }

    /// <summary>
    /// The launch handshake: initialize, launch, wait for the adapter's <c>initialized</c> event, send the breakpoints of each
    /// file, then <c>configurationDone</c>. Returns the verified state of each breakpoint sent.
    /// </summary>
    public async Task<IReadOnlyList<DebugBreakpointState>> LaunchAsync(
        JsonObject launchArguments,
        IReadOnlyDictionary<string, IReadOnlyList<int>> breakpoints,
        CancellationToken cancellationToken)
    {
        await _client.RequestAsync("initialize", new JsonObject
        {
            ["clientID"] = "iterminal",
            ["clientName"] = "iTerminal Development Studio",
            ["adapterID"] = "iterminal",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["pathFormat"] = "path",
            ["supportsVariableType"] = true,
        }, cancellationToken);

        // The launch request is sent now and answered after configurationDone, as VS Code does; awaiting it first would deadlock.
        Task<JsonElement> launch = _client.RequestAsync("launch", launchArguments, cancellationToken);
        await Task.WhenAny(_initialized.Task, _ended.Task, Task.Delay(InitializeTimeout, cancellationToken));
        if (!_initialized.Task.IsCompleted)
        {
            throw new InvalidOperationException(_ended.Task.IsCompleted
                ? _ended.Task.Result
                : "The debugger did not become ready in time.");
        }

        var verified = new List<DebugBreakpointState>();
        foreach (var (path, lines) in breakpoints)
        {
            verified.AddRange(await SetBreakpointsAsync(path, lines, cancellationToken));
        }

        await _client.RequestAsync("configurationDone", new JsonObject(), cancellationToken);
        await launch;
        return verified;
    }

    /// <summary>Sends the complete breakpoint list of one file. The adapter replaces the file's earlier list with it.</summary>
    public async Task<IReadOnlyList<DebugBreakpointState>> SetBreakpointsAsync(string path, IEnumerable<int> lines, CancellationToken cancellationToken = default)
    {
        var breakpointArray = new JsonArray();
        foreach (int line in lines)
        {
            breakpointArray.Add(new JsonObject { ["line"] = line });
        }

        JsonElement body = await _client.RequestAsync("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = path, ["name"] = Path.GetFileName(path) },
            ["breakpoints"] = breakpointArray,
            ["sourceModified"] = false,
        }, cancellationToken);

        var result = new List<DebugBreakpointState>();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("breakpoints", out var items))
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                int line = item.TryGetProperty("line", out var l) ? l.GetInt32() : 0;
                bool verified = item.TryGetProperty("verified", out var v) && v.GetBoolean();
                result.Add(new DebugBreakpointState(path, line, verified));
            }
        }

        return result;
    }

    public Task ContinueAsync(CancellationToken cancellationToken = default) =>
        _client.RequestAsync("continue", new JsonObject { ["threadId"] = _threadId }, cancellationToken);

    public Task StepOverAsync(CancellationToken cancellationToken = default) =>
        _client.RequestAsync("next", new JsonObject { ["threadId"] = _threadId }, cancellationToken);

    public Task StepInAsync(CancellationToken cancellationToken = default) =>
        _client.RequestAsync("stepIn", new JsonObject { ["threadId"] = _threadId }, cancellationToken);

    public Task StepOutAsync(CancellationToken cancellationToken = default) =>
        _client.RequestAsync("stepOut", new JsonObject { ["threadId"] = _threadId }, cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken = default) =>
        _client.RequestAsync("pause", new JsonObject { ["threadId"] = _threadId }, cancellationToken);

    /// <summary>The frames of the thread, innermost first.</summary>
    public async Task<IReadOnlyList<DebugStackFrame>> StackTraceAsync(CancellationToken cancellationToken = default)
    {
        JsonElement body = await _client.RequestAsync("stackTrace", new JsonObject
        {
            ["threadId"] = _threadId,
            ["startFrame"] = 0,
            ["levels"] = 50,
        }, cancellationToken);

        var frames = new List<DebugStackFrame>();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("stackFrames", out var items))
        {
            foreach (JsonElement frame in items.EnumerateArray())
            {
                string path = frame.TryGetProperty("source", out var source) && source.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                frames.Add(new DebugStackFrame(
                    frame.GetProperty("id").GetInt32(),
                    frame.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    path,
                    frame.TryGetProperty("line", out var l) ? l.GetInt32() : 0));
            }
        }

        return frames;
    }

    /// <summary>The scopes of a frame (Locals, Globals, ...).</summary>
    public async Task<IReadOnlyList<DebugScope>> ScopesAsync(int frameId, CancellationToken cancellationToken = default)
    {
        JsonElement body = await _client.RequestAsync("scopes", new JsonObject { ["frameId"] = frameId }, cancellationToken);
        var scopes = new List<DebugScope>();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("scopes", out var items))
        {
            foreach (JsonElement scope in items.EnumerateArray())
            {
                scopes.Add(new DebugScope(
                    scope.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                    scope.TryGetProperty("variablesReference", out var r) ? r.GetInt32() : 0));
            }
        }

        return scopes;
    }

    /// <summary>The children of a variable or a scope.</summary>
    public async Task<IReadOnlyList<DebugVariable>> VariablesAsync(int reference, CancellationToken cancellationToken = default)
    {
        JsonElement body = await _client.RequestAsync("variables", new JsonObject { ["variablesReference"] = reference }, cancellationToken);
        var variables = new List<DebugVariable>();
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("variables", out var items))
        {
            foreach (JsonElement variable in items.EnumerateArray())
            {
                variables.Add(ReadVariable(variable));
            }
        }

        return variables;
    }

    /// <summary>
    /// Evaluates an expression in a frame, for a watch. A failure is returned as the value text (the adapter's message),
    /// so a watch that cannot be evaluated still shows why.
    /// </summary>
    public async Task<DebugVariable> EvaluateAsync(string expression, int frameId, CancellationToken cancellationToken = default)
    {
        try
        {
            JsonElement body = await _client.RequestAsync("evaluate", new JsonObject
            {
                ["expression"] = expression,
                ["frameId"] = frameId,
                ["context"] = "watch",
            }, cancellationToken);

            string value = body.TryGetProperty("result", out var result) ? result.GetString() ?? string.Empty : string.Empty;
            string type = body.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            int reference = body.TryGetProperty("variablesReference", out var r) ? r.GetInt32() : 0;
            return new DebugVariable(expression, value, type, reference);
        }
        catch (DapException exception)
        {
            return new DebugVariable(expression, exception.Message, string.Empty, 0);
        }
    }

    /// <summary>Ends the program and the adapter. Best effort: a session that already ended has nothing to terminate.</summary>
    public async Task TerminateAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _client.RequestAsync("disconnect", new JsonObject { ["terminateDebuggee"] = true }, timeout.Token);
        }
        catch (Exception exception) when (exception is DapException or OperationCanceledException or InvalidOperationException)
        {
            // The adapter did not answer; the process is killed below.
        }

        KillAdapter();
    }

    private void KillAdapter()
    {
        try
        {
            if (!_adapter.HasExited) _adapter.Kill();
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    private static DebugVariable ReadVariable(JsonElement variable) =>
        new(
            variable.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty,
            variable.TryGetProperty("value", out var v) ? v.GetString() ?? string.Empty : string.Empty,
            variable.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty,
            variable.TryGetProperty("variablesReference", out var r) ? r.GetInt32() : 0);

    private void OnEvent(DapEvent dapEvent)
    {
        switch (dapEvent.Name)
        {
            case "initialized":
                _initialized.TrySetResult();
                break;
            case "stopped":
                int threadId = dapEvent.Body.ValueKind == JsonValueKind.Object && dapEvent.Body.TryGetProperty("threadId", out var t)
                    ? t.GetInt32()
                    : _threadId;
                _threadId = threadId;
                string reason = dapEvent.Body.ValueKind == JsonValueKind.Object && dapEvent.Body.TryGetProperty("reason", out var r) ? r.GetString() ?? "stopped" : "stopped";
                _notify(new DebugStoppedNotification(new DebugStop(threadId, reason)));
                break;
            case "continued":
                _notify(new DebugContinuedNotification(_threadId));
                break;
            case "output":
                string category = dapEvent.Body.TryGetProperty("category", out var c) ? c.GetString() ?? "console" : "console";
                string text = dapEvent.Body.TryGetProperty("output", out var o) ? o.GetString() ?? string.Empty : string.Empty;
                _notify(new DebugOutputNotification(category, text));
                break;
            case "terminated":
            case "exited":
                MarkEnded("The program ended.");
                break;
        }
    }

    private void MarkEnded(string reason)
    {
        if (Interlocked.Exchange(ref _endedFlag, 1) != 0) return;
        _ended.TrySetResult(reason);
        _notify(new DebugEndedNotification(reason));
    }

    public async ValueTask DisposeAsync()
    {
        KillAdapter();
        await _client.DisposeAsync();
        _adapter.Dispose();
    }
}

/// <summary>
/// The debug adapter's process as a session uses it: the standard streams it speaks DAP over, its exit and its stderr lines.
/// The session takes this interface rather than a <see cref="Process"/>, so tests can drive a scripted adapter in memory.
/// </summary>
public interface IDebugAdapterProcess : IDisposable
{
    /// <summary>What the session writes its requests to (the adapter's standard input).</summary>
    Stream StandardInput { get; }

    /// <summary>What the session reads the adapter's messages from (the adapter's standard output).</summary>
    Stream StandardOutput { get; }

    /// <summary>The exit code, once the adapter has exited.</summary>
    int ExitCode { get; }

    bool HasExited { get; }

    /// <summary>Raised once when the adapter exits.</summary>
    event EventHandler? Exited;

    /// <summary>Raised for each line the adapter writes to its standard error.</summary>
    event EventHandler<string>? ErrorLine;

    /// <summary>Starts reading the standard error. Called after the session subscribed to <see cref="ErrorLine"/>.</summary>
    void BeginReading();

    /// <summary>Kills the adapter and everything it started.</summary>
    void Kill();
}

/// <summary>The production adapter: a real debugger process started by <see cref="DebugSession.Start"/>.</summary>
internal sealed class SystemDebugAdapterProcess : IDebugAdapterProcess
{
    private readonly Process _process;

    public SystemDebugAdapterProcess(Process process)
    {
        _process = process;
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) ErrorLine?.Invoke(this, e.Data); };
    }

    public Stream StandardInput => _process.StandardInput.BaseStream;

    public Stream StandardOutput => _process.StandardOutput.BaseStream;

    public int ExitCode => _process.ExitCode;

    public bool HasExited => _process.HasExited;

    public event EventHandler? Exited
    {
        add => _process.Exited += value;
        remove => _process.Exited -= value;
    }

    public event EventHandler<string>? ErrorLine;

    public void BeginReading() => _process.BeginErrorReadLine();

    public void Kill() => _process.Kill(entireProcessTree: true);

    public void Dispose() => _process.Dispose();
}
