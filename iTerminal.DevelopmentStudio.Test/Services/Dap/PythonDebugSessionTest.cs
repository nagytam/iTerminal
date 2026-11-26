using System.Diagnostics;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using iTerminal.DevelopmentStudio.Services.Dap;

namespace iTerminal.DevelopmentStudio.Test.Services.Dap;

/// <summary>
/// A real debugpy session: the adapter starts a small script, stops at a breakpoint, and the stack, the locals, a watch and
/// the end of the run are read back. It is inconclusive, not failed, on a machine where debugpy is not importable.
/// </summary>
[TestClass]
public class PythonDebugSessionTest
{
    private const string Script = "def add(a, b):\n    total = a + b\n    return total\n\nx = add(2, 3)\nprint('done', x)\n";

    private static bool DebugpyIsInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("python3", ["-c", "import debugpy"]) { UseShellExecute = false, CreateNoWindow = true });
            return process is not null && process.WaitForExit(20_000) && process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task BreakpointStopsTheScriptAndTheLocalsAndWatchAreRead()
    {
        if (!DebugpyIsInstalled()) Assert.Inconclusive("debugpy is not installed for python3.");

        string folder = Path.Combine(Path.GetTempPath(), $"DebugpyTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "prog.py");
        File.WriteAllText(path, Script);
        var stopped = new TaskCompletionSource<IDebugNotification>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new List<string>();
        try
        {
            var project = new DevelopmentProject("prog.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, path);
            var plan = new DevelopmentToolchains().For(project).PlanDebug(project, null, debugTests: false)!;

            await using var session = DebugSession.Start(plan, folder, notification =>
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

            await session.LaunchAsync(plan.Launch, new Dictionary<string, IReadOnlyList<int>> { [path] = [2] }, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(60));
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(60));

            var frames = await session.StackTraceAsync();
            var top = frames.First();
            Assert.AreEqual(2, top.Line, "the breakpoint is on line 2");
            Assert.AreEqual(path, top.Path);

            var scopes = await session.ScopesAsync(top.Id);
            var locals = new List<iTerminal.DevelopmentStudio.Model.DebugVariable>();
            foreach (var scope in scopes.Where(s => s.Name.Contains("Local", StringComparison.OrdinalIgnoreCase)))
            {
                locals.AddRange(await session.VariablesAsync(scope.Reference));
            }

            Assert.AreEqual("2", locals.Single(v => v.Name == "a").Value);
            Assert.AreEqual("3", locals.Single(v => v.Name == "b").Value);

            var watch = await session.EvaluateAsync("a * 10 + b", top.Id);
            Assert.AreEqual("23", watch.Value);

            await session.ContinueAsync();
            await ended.Task.WaitAsync(TimeSpan.FromSeconds(60));

            lock (output) StringAssert.Contains(string.Concat(output), "done 5");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
