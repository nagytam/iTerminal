using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using iTerminal.DevelopmentStudio.Dialogs;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.DevelopmentStudio.Test.Dialogs;

/// <summary>The File Explorer, the coverage view and the profile view. The fake runner writes the files the real tools would.</summary>
[TestClass]
public class DevelopmentStudioAnalysisDialogTest
{
    private static async Task<DevelopmentStudioDialog> OpenAsync(string folder, IReadOnlyList<DevelopmentProject> items, FakeProcessRunner runner)
    {
        var dialog = new DevelopmentStudioDialog(new FakeWorkspaceScanner(items), runner, new DevelopmentToolchains(), new BreakpointStore(),
            new MainProgramStore(), new FakeDotnetSolutionService { Sdks = [] }, TestTheme())
        {
            Folder = folder,
        };
        await ShowAsync(dialog);
        return dialog;
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudioAnalysis_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, "src"));
        File.WriteAllText(Path.Combine(folder, "src", "inner.txt"), "inner");
        File.WriteAllText(Path.Combine(folder, "root.txt"), "root");
        return folder;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFileExplorerListsFoldersFirstAndEnterExpandsAFolder()
    {
        string folder = NewFolder();
        try
        {
            var dialog = await OpenAsync(folder, [], new FakeProcessRunner());
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.F6));
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.F6));
            await WaitUntilAsync(() => dialog.ExplorerRows.Contains("▸ src"), "the folder tree to be read");

            CollectionAssert.AreEqual(new[] { "▸ src", "  root.txt" }, dialog.ExplorerRows.ToArray());

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

            CollectionAssert.AreEqual(new[] { "▾ src", "    inner.txt", "  root.txt" }, dialog.ExplorerRows.ToArray());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAFileOpensItInTheSourceView()
    {
        string folder = NewFolder();
        try
        {
            var dialog = await OpenAsync(folder, [], new FakeProcessRunner());
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.F6));
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.F6));
            await WaitUntilAsync(() => dialog.ExplorerRows.Contains("  root.txt"), "the folder tree to be read");
            await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

            await WaitUntilAsync(() => dialog.EditorLines.Any(line => line.Contains("root", StringComparison.Ordinal)), "the file to open");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CoverageOfAPythonScriptFillsTheCoverageViewAndTheGutter()
    {
        string folder = NewFolder();
        string script = Path.Combine(folder, "prog.py");
        File.WriteAllText(script, "x = 1\ny = 2\n");
        var item = new DevelopmentProject("prog.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, script);
        var runner = new FakeProcessRunner
        {
            OnRun = command =>
            {
                // "coverage xml -o <report>" writes the Cobertura report: line 1 ran, line 2 did not.
                int output = command.Arguments.ToList().IndexOf("-o");
                if (output < 0) return;
                File.WriteAllText(command.Arguments[output + 1], $"""
                    <coverage><sources><source>{folder}</source></sources><packages><package><classes>
                      <class filename="prog.py"><lines><line number="1" hits="1"/><line number="2" hits="0"/></lines></class>
                    </classes></package></packages></coverage>
                    """);
            },
        };
        try
        {
            var dialog = await OpenAsync(folder, [item], runner);

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.O, 'o'));
            await WaitUntilAsync(() => !dialog.IsBusy, "the coverage run to finish");

            StringAssert.StartsWith(dialog.Status, "Coverage 50.0%: 1 of 2 lines in 1 files.");
            CollectionAssert.Contains(dialog.OutputLines.ToList(), "Coverage 50.0%: 1 of 2 lines in 1 files.");
            Assert.AreEqual(2, runner.Commands.Count, "coverage run, then the report");
            StringAssert.Contains(runner.Commands[0].Arguments.Last(), "prog.py");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProfileOfAPythonScriptFillsTheProfileView()
    {
        string folder = NewFolder();
        string script = Path.Combine(folder, "prog.py");
        File.WriteAllText(script, "print(1)\n");
        var item = new DevelopmentProject("prog.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, script);
        var runner = new FakeProcessRunner
        {
            OnRun = command =>
            {
                int output = command.Arguments.ToList().IndexOf("-o");
                if (output >= 0) File.WriteAllText(command.Arguments[output + 1], "stats");
            },
            OutputFor = command => command.Arguments.Contains("-c")
                ? ["12\t0.001000\t0.050000\tprog.py:2(add)", "1\t0.000100\t0.060000\tprog.py:5(<module>)"]
                : [],
        };
        try
        {
            var dialog = await OpenAsync(folder, [item], runner);

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.P, 'p'));
            await WaitUntilAsync(() => !dialog.IsBusy, "the profile to finish");

            Assert.AreEqual("Profiled prog.py: 2 functions. Most time: prog.py:5(<module>).", dialog.Status);
            StringAssert.Contains(string.Join("\n", dialog.OutputLines), "prog.py:2(add)");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACleanRunWithNoCoverageSaysSoAndRunsNothing()
    {
        var item = new DevelopmentProject("Main.java", DevelopmentLanguage.Java, DevelopmentProjectKind.Script, "/src/Main.java");
        var runner = new FakeProcessRunner();
        var dialog = await OpenAsync(Path.GetTempPath(), [item], runner);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.O, 'o'));

        Assert.AreEqual("Java has no coverage measurement here.", dialog.Status);
        Assert.AreEqual(0, runner.Commands.Count);
    }
}
