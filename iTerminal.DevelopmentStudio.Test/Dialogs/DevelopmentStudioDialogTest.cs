using ScreenBuffer = iConsole.Test.Core.ScreenBuffer;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using iTerminal.DevelopmentStudio.Dialogs;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.DevelopmentStudio.Test.Dialogs;

[TestClass]
public class DevelopmentStudioDialogTest
{
    private static readonly DevelopmentProject Solution = new("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.Solution, "/src/App.slnx");
    private static readonly DevelopmentProject App = new("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App/App.csproj");
    private static readonly DevelopmentProject AppTest = new("App.Test", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App.Test/App.Test.csproj", IsTest: true);
    private static readonly DevelopmentProject Script = new("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, "/src/tool.py");
    private static readonly DevelopmentProject Java = new("svc", DevelopmentLanguage.Java, DevelopmentProjectKind.JavaProject, "/src/svc/pom.xml");

    private static readonly SolutionInfo TwoProjects = new(
        "/src/App.slnx", "App", "/src",
        [
            new SolutionProject("App", "App/App.csproj", "/src/App/App.csproj"),
            new SolutionProject("App.Test", "App.Test/App.Test.csproj", "/src/App.Test/App.Test.csproj", IsTestProject: true),
        ]);

    private sealed record Harness(DevelopmentStudioDialog Dialog, FakeProcessRunner Runner, FakeDotnetSolutionService Dotnet, BreakpointStore Breakpoints);

    private static async Task<Harness> OpenAsync(IReadOnlyList<DevelopmentProject> items, FakeDotnetSolutionService? dotnet = null, string? solutionPath = null, FakeProcessRunner? runner = null)
    {
        dotnet ??= new FakeDotnetSolutionService { Solution = TwoProjects, Sdks = [new DotnetSdk("9.0.100", "/sdk/9")] };
        runner ??= new FakeProcessRunner();
        var breakpoints = new BreakpointStore();
        var dialog = new DevelopmentStudioDialog(new FakeWorkspaceScanner(items), runner, new DevelopmentToolchains(), breakpoints, dotnet, TestTheme())
        {
            Folder = "/src",
        };
        if (solutionPath is not null) dialog.SolutionPath = solutionPath;
        await ShowAsync(dialog);
        return new Harness(dialog, runner, dotnet, breakpoints);
    }

    private static async Task WaitIdleAsync(DevelopmentStudioDialog dialog) =>
        await WaitUntilAsync(() => !dialog.IsBusy, "the command to finish");

    private static async Task WaitForStatusAsync(DevelopmentStudioDialog dialog, string text) =>
        await WaitUntilAsync(() => dialog.Status.Contains(text, StringComparison.Ordinal), $"the status line to show '{text}'");

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExplorerListsSolutionProjectsAndScriptsWithCheckboxesAndTestTags()
    {
        var harness = await OpenAsync([Solution, App, AppTest, Script, Java]);

        CollectionAssert.AreEqual(new[]
        {
            "Solution  App",
            "   [ ] C#  App",
            "   [ ] C#  App.Test  [test]",
            "       Python  tool.py",
            "       Java  svc",
        }, harness.Dialog.ExplorerRows.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningASolutionListsOnlyThatSolutionAndItsProjects()
    {
        var harness = await OpenAsync([Solution, App, AppTest, Script], solutionPath: "/src/App.slnx");

        CollectionAssert.AreEqual(new[] { "Solution  App", "   [ ] C#  App", "   [ ] C#  App.Test  [test]" }, harness.Dialog.ExplorerRows.ToArray());
        Assert.AreEqual("/src", harness.Dialog.Folder);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildOnTheSolutionRunsTheSelectedSdkHostAndWritesItsOutput()
    {
        var runner = new FakeProcessRunner { Output = ["Build succeeded."] };
        var harness = await OpenAsync([Solution, App, AppTest], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        var command = runner.Commands.Single();
        Assert.AreEqual(new DotnetSdk("9.0.100", "/sdk/9").HostPath, command.FileName);
        CollectionAssert.AreEqual(new[] { "build", "/src/App.slnx" }, command.Arguments.ToArray());
        CollectionAssert.Contains(harness.Dialog.OutputLines.ToList(), "Build succeeded.");
        await WaitForStatusAsync(harness.Dialog, "build succeeded");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SpaceChecksProjectsAndBuildRunsOnlyTheCheckedOnes()
    {
        var runner = new FakeProcessRunner();
        var harness = await OpenAsync([Solution, App, AppTest], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        Assert.AreEqual("   [x] C#  App", harness.Dialog.ExplorerRows[1]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        var command = runner.Commands.Single();
        CollectionAssert.AreEqual(new[] { "build", "/src/App/App.csproj" }, command.Arguments.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftTChecksTheTestProjectsAndAgainTogglesThemOff()
    {
        var harness = await OpenAsync([Solution, App, AppTest]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.T, 'T', shift: true));

        Assert.AreEqual("   [x] C#  App.Test  [test]", harness.Dialog.ExplorerRows[2]);
        Assert.AreEqual("   [ ] C#  App", harness.Dialog.ExplorerRows[1], "Shift+T checks only the test projects");

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.T, 'T', shift: true));

        Assert.AreEqual("   [ ] C#  App.Test  [test]", harness.Dialog.ExplorerRows[2]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SWritesTheSbomAsXmlByDefaultAndAsJsonAfterF()
    {
        var dotnet = new FakeDotnetSolutionService { Solution = TwoProjects, Sdks = [] };
        var harness = await OpenAsync([Solution, App, AppTest], dotnet: dotnet);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 's'));
        await WaitForStatusAsync(harness.Dialog, "Wrote an SBOM");
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F, 'f'));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 's'));
        await WaitUntilAsync(() => dotnet.SbomRequests.Count == 2, "the second SBOM");

        CollectionAssert.AreEqual(new[] { SbomFormat.Xml, SbomFormat.Json }, dotnet.SbomRequests.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VCyclesTheSdkAndTheNextCommandRunsOnIt()
    {
        var dotnet = new FakeDotnetSolutionService
        {
            Solution = TwoProjects,
            Sdks = [new DotnetSdk("9.0.100", "/sdk/9"), new DotnetSdk("8.0.400", "/sdk/8")],
        };
        var runner = new FakeProcessRunner();
        var harness = await OpenAsync([Solution, App, AppTest], dotnet: dotnet, runner: runner);
        Assert.AreEqual("9.0.100", harness.Dialog.SelectedSdk!.Version, "the newest stable SDK is the default");

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v'));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        Assert.AreEqual("/sdk/8/dotnet".Replace('/', Path.DirectorySeparatorChar), runner.Commands.Single().FileName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATestRunSumsTheVsTestTotalsOfItsTargets()
    {
        var runner = new FakeProcessRunner
        {
            Output = ["Passed!  - Failed: 0, Passed: 4, Skipped: 1, Total: 5, Duration: 1 s - App.Test.dll"],
        };
        var harness = await OpenAsync([Solution, App, AppTest], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.T, 't'));
        await WaitIdleAsync(harness.Dialog);

        Assert.AreEqual("Tests: 4 passed, 0 failed, 1 skipped.", harness.Dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CleanOfAPythonScriptRemovesItsCacheFolders()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        string cache = Path.Combine(folder, "__pycache__");
        Directory.CreateDirectory(cache);
        try
        {
            var script = new DevelopmentProject("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, Path.Combine(folder, "tool.py"));
            var harness = await OpenAsync([script]);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.C, 'c'));

            await WaitForStatusAsync(harness.Dialog, "Removed 1 cache folders");
            Assert.IsFalse(Directory.Exists(cache));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildOfAJavaProjectRunsMaven()
    {
        var runner = new FakeProcessRunner();
        var harness = await OpenAsync([Java], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        var command = runner.Commands.Single();
        Assert.AreEqual("mvn", command.FileName);
        CollectionAssert.AreEqual(new[] { "-B", "-q", "compile" }, command.Arguments.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F9SetsABreakpointOnTheCursorLineOfTheOpenFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "tool.py");
        File.WriteAllText(path, "print('hi')\nprint('bye')\n");
        try
        {
            var script = new DevelopmentProject("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, path);
            var harness = await OpenAsync([script]);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
            await WaitUntilAsync(() => harness.Dialog.EditorLines.Any(line => line.Contains("print('hi')", StringComparison.Ordinal)), "the file to open");
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F9));

            Assert.IsTrue(harness.Breakpoints.Contains(path, 1));
            StringAssert.StartsWith(harness.Dialog.EditorLines[0], "●");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DebuggingPowerShellSaysItIsNotOffered()
    {
        var script = new DevelopmentProject("tool.ps1", DevelopmentLanguage.PowerShell, DevelopmentProjectKind.Script, "/src/tool.ps1");
        var harness = await OpenAsync([script]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F5));
        await WaitForStatusAsync(harness.Dialog, "is not available");

        StringAssert.Contains(harness.Dialog.Status, "PowerShell");
        Assert.IsFalse(harness.Dialog.IsDebugging);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHintsNameTheDebuggerKeysOfVisualStudio()
    {
        var harness = await OpenAsync([Solution]);
        var keys = harness.Dialog.GetKeyHints().Select(hint => hint.Key).ToList();

        CollectionAssert.IsSubsetOf(new[] { "F5", "Ctrl+F5", "Shift+F5", "F9", "F10 / F11", "Shift+F11", "Space", "A / Shift+T", "V", "S / F" }, keys);
    }

    /// <summary>The row and column of the first screen line that holds <paramref name="text"/>.</summary>
    private static (int Row, int Column) Locate(ScreenBuffer screen, string text)
    {
        for (int row = 0; row < iTerminal.Core.Test.Helper.UiTestHelper.ScreenSize.Height; row++)
        {
            int column = screen.Line(row).IndexOf(text, StringComparison.Ordinal);
            if (column >= 0) return (row, column);
        }

        Assert.Fail($"'{text}' is not on the screen");
        return (-1, -1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheEditorIsOnTheLeftTheSolutionExplorerOnTheRightAndTheOutputPanelAtTheBottom()
    {
        var harness = await OpenAsync([Solution, App]);

        var screen = Render(harness.Dialog);

        var editor = Locate(screen, " Source");
        var explorer = Locate(screen, " Solution Explorer");
        var output = Locate(screen, " Output ");
        Assert.AreEqual(editor.Row, explorer.Row, "the editor and the explorer share the top band");
        Assert.IsTrue(editor.Column < iTerminal.Core.Test.Helper.UiTestHelper.ScreenSize.Width / 2, "the editor is the left column");
        Assert.IsTrue(explorer.Column > iTerminal.Core.Test.Helper.UiTestHelper.ScreenSize.Width / 2, "the Solution Explorer is the right column");
        Assert.IsTrue(output.Row > explorer.Row + 5, "the output panel sits below the panes");
        Assert.AreEqual(1, output.Column, "the output tabs start at the left edge of the dialog, inside the frame");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F7CyclesTheOutputPanelTabsAndLeavesTheEditorOnTheSource()
    {
        var harness = await OpenAsync([Solution]);
        var editorBefore = harness.Dialog.EditorLines.ToList();

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));

        CollectionAssert.Contains(harness.Dialog.OutputLines.ToList(), "Watch", "F7 moves the output panel to the debug tab");
        CollectionAssert.AreEqual(editorBefore, harness.Dialog.EditorLines.ToList(), "the editor keeps the source");

        for (int i = 0; i < 3; i++) await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));

        CollectionAssert.AreEqual(editorBefore, harness.Dialog.EditorLines.ToList(), "the editor keeps the source through the whole cycle");
        CollectionAssert.DoesNotContain(harness.Dialog.OutputLines.ToList(), "Watch", "four presses come back to the output log");
    }
}
