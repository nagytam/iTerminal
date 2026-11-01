using iConsole.Data;
using iTerminal.Core.Dialogs.Dotnet;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Dotnet;

/// <summary>
/// F3 on a solution file. dotnet runs on a background task, so the fake service below can hold a
/// run open (to test the busy state) or finish it at once with scripted output.
/// </summary>
[TestClass]
public class SolutionViewerDialogTest
{
    private sealed class FakeDotnetSolutionService : IDotnetSolutionService
    {
        public SolutionInfo Solution { get; set; } = SolutionInfo.Empty;
        public Exception? ParseFailure { get; set; }
        public List<IReadOnlyList<string>> Runs { get; } = [];
        public List<string> Output { get; } = [];
        public int ExitCode { get; set; }
        public TaskCompletionSource? Gate { get; set; }

        public List<string> Hosts { get; } = [];
        public IReadOnlyList<DotnetSdk> Sdks { get; set; } = [];

        public IReadOnlyList<DotnetSdk> ListSdks() => Sdks;

        public SolutionInfo ParseSolution(string solutionPath) =>
            ParseFailure is not null ? throw ParseFailure : Solution;

        public async Task<int> RunAsync(string workingDirectory, string dotnetPath, IReadOnlyList<string> arguments, Action<string> onOutputLine, CancellationToken cancellationToken = default)
        {
            lock (Runs)
            {
                Runs.Add(arguments);
                Hosts.Add(dotnetPath);
            }

            foreach (var line in Output)
            {
                onOutputLine(line);
            }

            if (Gate is not null)
            {
                try
                {
                    await Gate.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return -1;
                }
            }

            return ExitCode;
        }
    }

    private static SolutionInfo TwoProjects() => new(
        "/src/App.slnx",
        "App",
        "/src",
        [
            new SolutionProject("App", "App/App.csproj", "/src/App/App.csproj"),
            new SolutionProject("App.Test", "App.Test/App.Test.csproj", "/src/App.Test/App.Test.csproj", IsTestProject: true),
        ]);

    private static async Task<SolutionViewerDialog> OpenAsync(FakeDotnetSolutionService service)
    {
        var dialog = new SolutionViewerDialog(service, TestTheme()) { SolutionPath = "/src/App.slnx" };
        await ShowAsync(dialog);
        return dialog;
    }

    private static Task WaitIdleAsync(SolutionViewerDialog dialog) =>
        WaitUntilAsync(() => !dialog.IsBusyForTest, "the dotnet run to finish");

    /// <summary>The host the product runs for an SDK root: dotnet, or dotnet.exe on Windows.</summary>
    private static string HostOf(string root) => Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListsTheSolutionRowAndEveryProject()
    {
        var dialog = await OpenAsync(new FakeDotnetSolutionService { Solution = TwoProjects() });

        var screen = Render(dialog);

        AssertScreenContains(screen, "[Solution] App");
        AssertScreenContains(screen, "[ ] App");
        AssertScreenContains(screen, "[ ] App.Test  [test]");
        AssertScreenContains(screen, "(2 projects)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheDialogFillsTheWholeScreen()
    {
        var dialog = await OpenAsync(new FakeDotnetSolutionService { Solution = TwoProjects() });

        Assert.AreEqual(dialog.Parent!.ViewSize, dialog.ViewSize);
        Assert.AreEqual(new Position(0, 0), dialog.Position);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASolutionThatCannotBeReadSaysWhyOnTheStatusLine()
    {
        // The error used to be written over the project list's first row, which the list then
        // repainted with its [Solution] row — so the reason never stayed on screen.
        var dialog = await OpenAsync(new FakeDotnetSolutionService { ParseFailure = new IOException("access denied") });

        StringAssert.Contains(dialog.StatusForTest, "access denied");
        AssertScreenContains(Render(dialog), "Failed to read solution: access denied");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptySolutionSaysSo()
    {
        var dialog = await OpenAsync(new FakeDotnetSolutionService
        {
            Solution = new SolutionInfo("/src/Empty.slnx", "Empty", "/src", []),
        });

        AssertScreenContains(Render(dialog), "No projects found in solution.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task KeyHintsStayVisibleAfterARunHasReportedItsStatus()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);
        AssertScreenContains(Render(dialog), "b:build");

        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);

        var screen = Render(dialog);
        AssertScreenContains(screen, "dotnet build succeeded.");
        AssertScreenContains(screen, "b:build", "the key hints must survive the first status message");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildOnTheSolutionRowBuildsTheSolution()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);

        CollectionAssert.AreEqual(new[] { "build", "/src/App.slnx" }, service.Runs.Single().ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SpaceChecksProjectsAndBuildRunsOncePerCheckedProject()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));
        AssertScreenContains(Render(dialog), "2 selected");

        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);

        Assert.AreEqual(2, service.Runs.Count);
        StringAssert.Contains(dialog.StatusForTest, "(2 projects)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASelectsAllAndAgainClearsTheSelection()
    {
        var dialog = await OpenAsync(new FakeDotnetSolutionService { Solution = TwoProjects() });

        await dialog.HandleKeyEventAsync(Char('a'));
        AssertScreenContains(Render(dialog), "2 selected");

        await dialog.HandleKeyEventAsync(Char('a'));
        AssertScreenDoesNotContain(Render(dialog), "selected)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CapitalTSelectsOnlyTheTestProjects()
    {
        var dialog = await OpenAsync(new FakeDotnetSolutionService { Solution = TwoProjects() });

        await dialog.HandleKeyEventAsync(Char('T'));

        var screen = Render(dialog);
        AssertScreenContains(screen, "[x] App.Test");
        AssertScreenContains(screen, "[ ] App  ", "the non-test project stays unchecked");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TestRunsReportTheAggregatedTotals()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        service.Output.Add("Passed!  - Failed: 0, Passed: 42, Skipped: 1, Total: 43, Duration: 1 s - App.Test.dll");
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('t'));
        await WaitIdleAsync(dialog);

        StringAssert.Contains(dialog.StatusForTest, "Tests: 42 passed, 0 failed, 1 skipped (43 total, 1 project)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedBuildReportsTheExitCode()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), ExitCode = 1 };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);

        Assert.AreEqual("dotnet build failed (exit 1).", dialog.StatusForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunOnTheSolutionRowExplainsThatAProjectIsNeeded()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('r'));

        StringAssert.Contains(dialog.StatusForTest, "Select a project to run");
        Assert.AreEqual(0, service.Runs.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunOnAProjectRowRunsThatProject()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Char('r'));
        await WaitIdleAsync(dialog);

        CollectionAssert.AreEqual(new[] { "run", "--project", "/src/App/App.csproj" }, service.Runs.Single().ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CleanOnTheSolutionRowCleansTheSolution()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('c'));
        await WaitIdleAsync(dialog);

        CollectionAssert.AreEqual(new[] { "clean", "/src/App.slnx" }, service.Runs.Single().ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CleanOnAProjectRowCleansThatProject()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await dialog.HandleKeyEventAsync(Char('c'));
        await WaitIdleAsync(dialog);

        CollectionAssert.AreEqual(new[] { "clean", "/src/App/App.csproj" }, service.Runs.Single().ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CleanReportsSuccessOnTheStatusLine()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('c'));
        await WaitIdleAsync(dialog);

        Assert.AreEqual("dotnet clean succeeded.", dialog.StatusForTest);
    }

    private static readonly DotnetSdk Sdk913 = new("9.0.300", "/usr/lib/dotnet");
    private static readonly DotnetSdk Sdk1013 = new("10.0.113", "/opt/dotnet");
    private static readonly DotnetSdk Sdk10Preview = new("10.0.200-preview.1", "/home/me/dotnet");

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheNewestStableSdkIsSelectedByDefault()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), Sdks = [Sdk10Preview, Sdk913, Sdk1013] };
        var dialog = await OpenAsync(service);

        Assert.AreEqual("10.0.113", dialog.SdkVersionForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHeaderShowsTheSdkVersion()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), Sdks = [Sdk1013] };
        var dialog = await OpenAsync(service);

        var screen = Render(dialog);

        AssertScreenContains(screen, ".NET SDK 10.0.113");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BuildUsesTheSelectedSdksHost()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), Sdks = [Sdk1013] };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);

        Assert.AreEqual(HostOf("/opt/dotnet"), service.Hosts.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VCyclesThroughTheInstalledSdks_AndTheNextCommandUsesTheChosenOne()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), Sdks = [Sdk1013, Sdk913] };
        var dialog = await OpenAsync(service);
        Assert.AreEqual("10.0.113", dialog.SdkVersionForTest);

        await dialog.HandleKeyEventAsync(Char('v'));
        Assert.AreEqual("9.0.300", dialog.SdkVersionForTest);
        StringAssert.Contains(dialog.StatusForTest, "9.0.300");

        await dialog.HandleKeyEventAsync(Char('v'));
        Assert.AreEqual("10.0.113", dialog.SdkVersionForTest, "it wraps around");

        await dialog.HandleKeyEventAsync(Char('v'));
        await dialog.HandleKeyEventAsync(Char('t'));
        await WaitIdleAsync(dialog);
        Assert.AreEqual(HostOf("/usr/lib/dotnet"), service.Hosts.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VWithOneSdkSaysSo()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), Sdks = [Sdk1013] };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('v'));

        StringAssert.Contains(dialog.StatusForTest, "Only .NET SDK 10.0.113");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoSdkFoundCommandsUseTheDotnetOnThePath()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);

        Assert.AreEqual("dotnet", service.Hosts.Single());
        Assert.AreEqual(string.Empty, dialog.SdkVersionForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASecondCommandWhileBusyIsExplainedNotSwallowed()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), Gate = new TaskCompletionSource() };
        var dialog = await OpenAsync(service);
        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitUntilAsync(() => service.Runs.Count == 1, "the build to start");

        await dialog.HandleKeyEventAsync(Char('t'));

        StringAssert.Contains(dialog.StatusForTest, "still running");
        Assert.AreEqual(1, service.Runs.Count, "no second dotnet process");
        service.Gate.SetResult();
        await WaitIdleAsync(dialog);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task XCancelsARunningCommand()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects(), Gate = new TaskCompletionSource() };
        var dialog = await OpenAsync(service);
        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitUntilAsync(() => service.Runs.Count == 1, "the build to start");

        await dialog.HandleKeyEventAsync(Char('x'));
        await WaitIdleAsync(dialog);

        Assert.AreEqual("dotnet build cancelled.", dialog.StatusForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OutputIsShownWithTheCommandLine()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        service.Output.Add("Build succeeded.");
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);

        CollectionAssert.Contains(dialog.OutputForTest.ToList(), "$ dotnet build /src/App.slnx");
        AssertScreenContains(Render(dialog), "Build succeeded.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesTheDialog()
    {
        var dialog = new SolutionViewerDialog(new FakeDotnetSolutionService { Solution = TwoProjects() }, TestTheme()) { SolutionPath = "/src/App.slnx" };
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheOutputScrollsWithPageKeysAndHomeAndEndJumpToTheEnds()
    {
        var service = new FakeDotnetSolutionService { Solution = TwoProjects() };
        service.Output.AddRange(Enumerable.Range(1, 200).Select(i => $"line {i}"));
        var dialog = await OpenAsync(service);
        await dialog.HandleKeyEventAsync(Char('b'));
        await WaitIdleAsync(dialog);
        var bottom = dialog.OutputScrollForTest;
        Assert.IsTrue(bottom > 0, "a long run follows its tail");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(bottom - 10, dialog.OutputScrollForTest);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, dialog.OutputScrollForTest);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(10, dialog.OutputScrollForTest);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(bottom, dialog.OutputScrollForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CapitalTWithNoTestProjectsSaysSo()
    {
        var service = new FakeDotnetSolutionService
        {
            Solution = new SolutionInfo("/src/App.slnx", "App", "/src", [new SolutionProject("App", "App/App.csproj", "/src/App/App.csproj")]),
        };
        var dialog = await OpenAsync(service);

        await dialog.HandleKeyEventAsync(Char('T'));

        Assert.AreEqual("No test projects in this solution.", dialog.StatusForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CapitalTAgainClearsTheTestProjects()
    {
        var dialog = await OpenAsync(new FakeDotnetSolutionService { Solution = TwoProjects() });

        await dialog.HandleKeyEventAsync(Char('T'));
        await dialog.HandleKeyEventAsync(Char('T'));

        AssertScreenContains(Render(dialog), "[ ] App.Test");
    }
}
