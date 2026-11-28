using System.Text.Json.Nodes;
using iTerminal.DevelopmentStudio.Services.Dap;

namespace iTerminal.DevelopmentStudio.Test.Services.Dap;

/// <summary>
/// The debug session over an in-memory adapter: it answers the requests of the launch handshake, stops at the breakpoint,
/// and serves the stack, the locals, a watch and the end of the run. No debugger process is started.
/// </summary>
[TestClass]
public class DebugSessionTest
{
    private const string Program = "/src/prog.py";

    /// <summary>
    /// Stands in for debugpy: it answers each request the session sends and raises the events a run of the program at
    /// <see cref="Program"/> would, a breakpoint on line 2 and the end of the program after the continue.
    /// </summary>
    private sealed class FakeDebugger : IDebugAdapterProcess
    {
        private readonly Wiring _wiring = new();
        private readonly FakeAdapter _adapter;
        private readonly Task _loop;

        public FakeDebugger()
        {
            _adapter = new FakeAdapter(_wiring.AdapterReads, _wiring.AdapterWrites);
            _loop = Task.Run(Serve);
        }

        public Stream StandardInput => _wiring.ClientWrites;

        public Stream StandardOutput => _wiring.ClientReads;

        public int ExitCode => 0;

        public bool HasExited { get; private set; }

        public event EventHandler? Exited;

        public event EventHandler<string>? ErrorLine
        {
            add { }
            remove { }
        }

        public void BeginReading()
        {
        }

        public void Kill()
        {
            if (HasExited) return;
            HasExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Kill();
            _wiring.Dispose();
            _loop.Wait(TimeSpan.FromSeconds(5));
        }

        private void Serve()
        {
            try
            {
                while (true) Answer(_adapter.ReadRequest().AsObject());
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException or OverflowException)
            {
                // The session disposed the pipes: the adapter is gone.
            }
        }

        private void Answer(JsonObject request)
        {
            string command = request["command"]!.GetValue<string>();
            switch (command)
            {
                case "initialize":
                    Respond(request, new JsonObject { ["supportsConfigurationDoneRequest"] = true });
                    break;
                case "launch":
                    Respond(request);
                    Event("initialized", new JsonObject());
                    break;
                case "setBreakpoints":
                    var breakpoints = new JsonArray();
                    foreach (JsonNode? breakpoint in request["arguments"]!["breakpoints"]!.AsArray())
                    {
                        breakpoints.Add(new JsonObject { ["line"] = breakpoint!["line"]!.GetValue<int>(), ["verified"] = true });
                    }

                    Respond(request, new JsonObject { ["breakpoints"] = breakpoints });
                    break;
                case "configurationDone":
                    Respond(request);
                    Event("stopped", new JsonObject { ["threadId"] = 1, ["reason"] = "breakpoint" });
                    break;
                case "stackTrace":
                    Respond(request, new JsonObject
                    {
                        ["stackFrames"] = new JsonArray(new JsonObject
                        {
                            ["id"] = 1,
                            ["name"] = "add",
                            ["line"] = 2,
                            ["source"] = new JsonObject { ["path"] = Program },
                        }),
                    });
                    break;
                case "scopes":
                    Respond(request, new JsonObject
                    {
                        ["scopes"] = new JsonArray(new JsonObject { ["name"] = "Locals", ["variablesReference"] = 2 }),
                    });
                    break;
                case "variables":
                    Respond(request, new JsonObject
                    {
                        ["variables"] = new JsonArray(
                            new JsonObject { ["name"] = "a", ["value"] = "2", ["type"] = "int", ["variablesReference"] = 0 },
                            new JsonObject { ["name"] = "b", ["value"] = "3", ["type"] = "int", ["variablesReference"] = 0 }),
                    });
                    break;
                case "evaluate":
                    Respond(request, new JsonObject { ["result"] = "23", ["type"] = "int", ["variablesReference"] = 0 });
                    break;
                case "continue":
                    Respond(request, new JsonObject { ["allThreadsContinued"] = true });
                    Event("output", new JsonObject { ["category"] = "stdout", ["output"] = "done 5\n" });
                    Event("terminated", new JsonObject());
                    break;
                case "disconnect":
                    Respond(request);
                    break;
                default:
                    _adapter.Write(new JsonObject
                    {
                        ["seq"] = 1,
                        ["type"] = "response",
                        ["request_seq"] = request["seq"]!.GetValue<int>(),
                        ["success"] = false,
                        ["command"] = command,
                        ["message"] = $"The fake debugger does not answer {command}.",
                    });
                    break;
            }
        }

        private void Respond(JsonObject request, JsonObject? body = null) => _adapter.Write(new JsonObject
        {
            ["seq"] = 1,
            ["type"] = "response",
            ["request_seq"] = request["seq"]!.GetValue<int>(),
            ["success"] = true,
            ["command"] = request["command"]!.GetValue<string>(),
            ["body"] = body ?? new JsonObject(),
        });

        private void Event(string name, JsonObject body) => _adapter.Write(new JsonObject
        {
            ["seq"] = 1,
            ["type"] = "event",
            ["event"] = name,
            ["body"] = body,
        });
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BreakpointStopsTheProgramAndTheLocalsWatchAndTheEndAreRead()
    {
        var stopped = new TaskCompletionSource<IDebugNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new List<string>();
        await using var session = DebugSession.Attach(new FakeDebugger(), notification =>
        {
            switch (notification)
            {
                case DebugStoppedNotification:
                    stopped.TrySetResult(notification);
                    break;
                case DebugOutputNotification o:
                    lock (output) output.Add(o.Text);
                    break;
                case DebugEndedNotification:
                    ended.TrySetResult();
                    break;
            }
        });

        var verified = await session.LaunchAsync(new JsonObject { ["program"] = Program }, new Dictionary<string, IReadOnlyList<int>> { [Program] = [2] }, CancellationToken.None);
        Assert.IsTrue(verified.Single().Verified, "the breakpoint on line 2 is verified");

        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var frames = await session.StackTraceAsync();
        Assert.AreEqual(2, frames.First().Line, "the breakpoint is on line 2");
        Assert.AreEqual(Program, frames.First().Path);

        var scopes = await session.ScopesAsync(frames.First().Id);
        var locals = new List<iTerminal.DevelopmentStudio.Model.DebugVariable>();
        foreach (var scope in scopes.Where(s => s.Name.Contains("Local", StringComparison.OrdinalIgnoreCase)))
        {
            locals.AddRange(await session.VariablesAsync(scope.Reference));
        }

        Assert.AreEqual("2", locals.Single(v => v.Name == "a").Value);
        Assert.AreEqual("3", locals.Single(v => v.Name == "b").Value);
        Assert.AreEqual("23", (await session.EvaluateAsync("a * 10 + b", frames.First().Id)).Value);

        await session.ContinueAsync();
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(20));
        lock (output) StringAssert.Contains(string.Concat(output), "done 5");
    }
}
