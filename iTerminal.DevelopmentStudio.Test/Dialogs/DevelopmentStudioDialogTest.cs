using ScreenBuffer = iConsole.Test.Core.ScreenBuffer;
using iConsole.Controls;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using iTerminal.DevelopmentStudio.Dialogs;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using iTerminal.DevelopmentStudio.Services.Lsp;
using iTerminal.DevelopmentStudio.Test.Services.Lsp;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.DevelopmentStudio.Test.Dialogs;

[TestClass]
public class DevelopmentStudioDialogTest
{
    private static readonly DevelopmentProject Solution = new("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.Solution, "/src/App.slnx");
    private static readonly DevelopmentProject App = new("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App/App.csproj", SolutionPath: "/src/App.slnx");
    private static readonly DevelopmentProject AppTest = new("App.Test", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App.Test/App.Test.csproj", IsTest: true, SolutionPath: "/src/App.slnx");
    private static readonly DevelopmentProject Script = new("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, "/src/tool.py");
    private static readonly DevelopmentProject Java = new("svc", DevelopmentLanguage.Java, DevelopmentProjectKind.JavaProject, "/src/svc/pom.xml");

    private static readonly SolutionInfo TwoProjects = new(
        "/src/App.slnx", "App", "/src",
        [
            new SolutionProject("App", "App/App.csproj", "/src/App/App.csproj"),
            new SolutionProject("App.Test", "App.Test/App.Test.csproj", "/src/App.Test/App.Test.csproj", IsTestProject: true),
        ]);

    private sealed record Harness(DevelopmentStudioDialog Dialog, FakeProcessRunner Runner, FakeDotnetSolutionService Dotnet, BreakpointStore Breakpoints);

    private static async Task<Harness> OpenAsync(IReadOnlyList<DevelopmentProject> items, FakeDotnetSolutionService? dotnet = null, string? solutionPath = null, FakeProcessRunner? runner = null, MainProgramStore? mainProgram = null, CSharpLanguageServer? server = null, FakeSettingsService? settings = null)
    {
        dotnet ??= new FakeDotnetSolutionService { Solution = TwoProjects, Sdks = [new DotnetSdk("9.0.100", "/sdk/9")] };
        runner ??= new FakeProcessRunner();
        var breakpoints = new BreakpointStore(new TestDatabase());
        var dialog = new DevelopmentStudioDialog(new FakeWorkspaceScanner(items), runner, new DevelopmentToolchains(), breakpoints, mainProgram ?? new MainProgramStore(), dotnet, TestTheme(), server ?? new CSharpLanguageServer(new FakeLanguageServer()), SyntaxHighlightingService.Default, new FakeSdkManager(), settings ?? new FakeSettingsService(), new FakeNuGetPackageService(), new FakePromptService())
        {
            Folder = "/src",
        };
        if (solutionPath is not null) dialog.SolutionPath = solutionPath;
        await ShowAsync(dialog);
        return new Harness(dialog, runner, dotnet, breakpoints);
    }

    private static async Task WaitIdleAsync(DevelopmentStudioDialog dialog) =>
        await WaitUntilAsync(() => !dialog.IsBusy, "the command to finish");

    /// <summary>
    /// Waits for a condition on the editor. A file read in the background is put in the editor on the UI thread's idle pass, so
    /// each check runs one idle pass first, as the application's loop does.
    /// </summary>
    private static async Task WaitUntilOpenAsync(DevelopmentStudioDialog dialog, Func<bool> condition, string because) =>
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return condition();
        }, because);

    private static async Task WaitForStatusAsync(DevelopmentStudioDialog dialog, string text) =>
        await WaitUntilAsync(() => dialog.Status.Contains(text, StringComparison.Ordinal), $"the status line to show '{text}'");

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExplorerListsSolutionProjectsAndScriptsWithCheckboxesAndTestTags()
    {
        var harness = await OpenAsync([Solution, App, AppTest, Script, Java]);

        CollectionAssert.AreEqual(new[]
        {
            "▾ Solution  App",
            "  ▸ [ ] C#  App",
            "  ▸ [ ] C#  App.Test  [test]",
            "  Python  tool.py",
            "▸ Java  svc",
        }, harness.Dialog.ExplorerRows.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningASolutionListsOnlyThatSolutionAndItsProjects()
    {
        var harness = await OpenAsync([Solution, App, AppTest, Script], solutionPath: "/src/App.slnx");

        CollectionAssert.AreEqual(new[] { "▾ Solution  App", "  ▸ [ ] C#  App", "  ▸ [ ] C#  App.Test  [test]" }, harness.Dialog.ExplorerRows.ToArray());
        Assert.AreEqual(Path.GetDirectoryName("/src/App.slnx"), harness.Dialog.Folder);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftAndRightCollapseAndExpandTheSolutionAndItsProjects()
    {
        var harness = await OpenAsync([Solution, App, AppTest, Script]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        CollectionAssert.AreEqual(new[] { "▸ Solution  App", "  Python  tool.py" }, harness.Dialog.ExplorerRows.ToArray(), "Left collapses the solution under the cursor");

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        CollectionAssert.AreEqual(new[]
        {
            "▾ Solution  App",
            "  ▸ [ ] C#  App",
            "  ▸ [ ] C#  App.Test  [test]",
            "  Python  tool.py",
        }, harness.Dialog.ExplorerRows.ToArray(), "Right expands it again");

        // A script has nothing to expand: Right leaves the list as it is.
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(4, harness.Dialog.ExplorerRows.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAProjectListsItsFilesAndFoldersAndLeavesOutAnotherProjectsFolder()
    {
        string folder = NewProjectFolder();
        try
        {
            var project = new DevelopmentProject("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, Path.Combine(folder, "App.csproj"));
            var nested = new DevelopmentProject("Lib", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, Path.Combine(folder, "Lib", "Lib.csproj"));
            var harness = await OpenAsync([project, nested]);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
            await WaitUntilAsync(() => !harness.Dialog.ExplorerRows.Any(row => row.Contains("reading the folder", StringComparison.Ordinal)), "the project folder to be read");

            CollectionAssert.AreEqual(new[]
            {
                "▾ [ ] C#  App",
                "  ▸ Properties",
                "    App.csproj",
                "    Program.cs",
                "▸ [ ] C#  Lib",
            }, harness.Dialog.ExplorerRows.ToArray(), "the Lib folder is drawn under its own project, not under App");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MMakesTheMainProgramAndCtrlF5RunsItWhateverTheCursorIs()
    {
        var runner = new FakeProcessRunner();
        var mainProgram = new MainProgramStore();
        var harness = await OpenAsync([Solution, App, AppTest], runner: runner, mainProgram: mainProgram);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.M, 'm'));
        Assert.IsTrue(harness.Dialog.Status.Contains("cannot be the Main program", StringComparison.Ordinal), "a solution is not a Main program");

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.M, 'm'));
        Assert.AreEqual("/src/App/App.csproj", mainProgram.Path);
        Assert.AreEqual("  ▸ [ ] C#  App  [main]", harness.Dialog.ExplorerRows[1]);

        // Ctrl+F5 runs the Main program, not the project under the cursor.
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F5, control: true));
        await WaitIdleAsync(harness.Dialog);
        Assert.IsTrue(runner.Commands.Single().Arguments.Contains("/src/App/App.csproj"));

        // The choice survives closing the studio: a new dialog on the same store shows it.
        var reopened = await OpenAsync([Solution, App, AppTest], mainProgram: mainProgram);
        Assert.AreEqual("  ▸ [ ] C#  App  [main]", reopened.Dialog.ExplorerRows[1]);

        await reopened.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await reopened.Dialog.HandleKeyEventAsync(Key(ConsoleKey.M, 'm'));
        Assert.AreEqual(string.Empty, mainProgram.Path, "a second M on the Main program clears it");
        Assert.AreEqual("  ▸ [ ] C#  App", reopened.Dialog.ExplorerRows[1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MRefusesALibraryButAcceptsAConsoleAndAWebProject()
    {
        var library = new DevelopmentProject("Lib", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/Lib/Lib.csproj", SolutionPath: "/src/App.slnx", Output: DotNetOutputKind.Library);
        var web = new DevelopmentProject("Site", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/Site/Site.csproj", SolutionPath: "/src/App.slnx", Output: DotNetOutputKind.Web);
        var mainProgram = new MainProgramStore();
        var harness = await OpenAsync([Solution, library, web, App], mainProgram: mainProgram);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.M, 'm'));
        Assert.AreEqual(string.Empty, mainProgram.Path, "a library has no program to start");
        Assert.IsTrue(harness.Dialog.Status.Contains("no program to start", StringComparison.Ordinal));

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.M, 'm'));
        Assert.AreEqual("/src/Site/Site.csproj", mainProgram.Path, "a web project can be the Main program");
    }

    /// <summary>A folder with <c>App/App.csproj</c>, a <c>Program.cs</c>, a <c>Properties</c> folder and a nested <c>Lib</c> project.</summary>
    private static string NewProjectFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudioTree_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(folder, "Properties"));
        Directory.CreateDirectory(Path.Combine(folder, "Lib"));
        File.WriteAllText(Path.Combine(folder, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(folder, "Program.cs"), "class Program {}");
        File.WriteAllText(Path.Combine(folder, "Properties", "launchSettings.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "Lib", "Lib.csproj"), "<Project />");
        return folder;
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
        CollectionAssert.AreEqual(new[] { "build", "/src/App.slnx", "-c", "Debug" }, command.Arguments.ToArray());
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
        Assert.AreEqual("  ▸ [x] C#  App", harness.Dialog.ExplorerRows[1]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        var command = runner.Commands.Single();
        CollectionAssert.AreEqual(new[] { "build", "/src/App/App.csproj", "-c", "Debug" }, command.Arguments.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftTChecksTheTestProjectsAndAgainTogglesThemOff()
    {
        var harness = await OpenAsync([Solution, App, AppTest]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.T, 'T', shift: true));

        Assert.AreEqual("  ▸ [x] C#  App.Test  [test]", harness.Dialog.ExplorerRows[2]);
        Assert.AreEqual("  ▸ [ ] C#  App", harness.Dialog.ExplorerRows[1], "Shift+T checks only the test projects");

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.T, 'T', shift: true));

        Assert.AreEqual("  ▸ [ ] C#  App.Test  [test]", harness.Dialog.ExplorerRows[2]);
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

        Assert.AreEqual(Path.Combine("/sdk/8", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), runner.Commands.Single().FileName);
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

        StringAssert.StartsWith(harness.Dialog.Status, "Tests: 4 passed, 0 failed, 1 skipped. Elapsed ");
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
    public async Task ALiveErrorShowsInTheErrorListAndAsAGutterMarkOnItsLine()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllText(path, "class P\n{\n    int x = \"ERROR\";\n}\n");
        try
        {
            var fake = new FakeLanguageServer
            {
                Diagnostics = (uri, text) => text.Contains("ERROR", StringComparison.Ordinal)
                    ? [FakeLanguageServer.Diagnostic(2, 12, 1, "CS0029", "Cannot implicitly convert")]
                    : [],
            };
            await using var server = new CSharpLanguageServer(fake);
            var script = new DevelopmentProject("Program.cs", DevelopmentLanguage.DotNet, DevelopmentProjectKind.Script, path);
            var harness = await OpenAsync([script], server: server);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
            // The dialog reads the server on its idle pass, as it does in the application.
            await WaitUntilAsync(() =>
            {
                harness.Dialog.OnIdle();
                return harness.Dialog.ErrorLines.Any(line => line.Contains("error CS0029", StringComparison.Ordinal));
            }, "the live error to reach the Error List");

            // The marks are rebuilt on the idle pass that sees the server's new version, one pass after the Error List has the error.
            await WaitUntilAsync(() =>
            {
                harness.Dialog.OnIdle();
                return harness.Dialog.EditorMark(3) == "!";
            }, "the line with the error to get the error mark");
            Assert.AreEqual("!", harness.Dialog.EditorMark(3), "the line with the error has the error mark");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABreakpointShowsBeforeALiveErrorOnTheSameLine()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllText(path, "class P\n{\n    int x = \"ERROR\";\n}\n");
        try
        {
            var fake = new FakeLanguageServer
            {
                Diagnostics = (uri, text) => text.Contains("ERROR", StringComparison.Ordinal)
                    ? [FakeLanguageServer.Diagnostic(2, 12, 1, "CS0029", "Cannot implicitly convert")]
                    : [],
            };
            await using var server = new CSharpLanguageServer(fake);
            var script = new DevelopmentProject("Program.cs", DevelopmentLanguage.DotNet, DevelopmentProjectKind.Script, path);
            var harness = await OpenAsync([script], server: server);
            harness.Breakpoints.Toggle(path, 3);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

            await WaitUntilAsync(() =>
            {
                harness.Dialog.OnIdle();
                return harness.Dialog.ErrorLines.Any(line => line.Contains("error CS0029", StringComparison.Ordinal));
            }, "the live error to reach the Error List");

            Assert.AreEqual("●", harness.Dialog.EditorMark(3), "the breakpoint wins over the error mark on its line");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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
            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.EditorLines.Any(line => line.Contains("print('hi')", StringComparison.Ordinal)), "the file to open");
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F9));

            Assert.IsTrue(harness.Breakpoints.Contains(path, 1));
            Assert.AreEqual("●", harness.Dialog.EditorMark(1));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DSwitchesToReleaseWhereF5RefusesToDebugAndBuildsInRelease()
    {
        var app = new DevelopmentProject("App", DevelopmentLanguage.DotNet, DevelopmentProjectKind.DotNetProject, "/src/App/App.csproj");
        var runner = new FakeProcessRunner();
        var harness = await OpenAsync([app], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.D, 'd'));
        await WaitForStatusAsync(harness.Dialog, "Build configuration: Release");
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F5));
        await WaitForStatusAsync(harness.Dialog, "Debugging is disabled");

        Assert.IsFalse(harness.Dialog.IsDebugging);
        Assert.AreEqual(0, runner.Commands.Count, "a refused debug starts no build");

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        CollectionAssert.AreEqual(new[] { "build", "/src/App/App.csproj", "-c", "Release" }, runner.Commands.Single().Arguments.ToArray());
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
    [Timeout(60_000)]
    [TestMethod]
    public async Task TheEditorColoursTheKeywordsStringsAndCommentsOfTheOpenSource()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "tool.py");
        File.WriteAllText(path, "def greet():\n    return 'hi'  # note\n");
        try
        {
            var script = new DevelopmentProject("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, path);
            var harness = await OpenAsync([script]);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.EditorLines.Any(line => line.Contains("return", StringComparison.Ordinal)), "the file to open");

            // The cursor starts on line 1 and the selected row is drawn in the selection colours, so the test reads line 2.
            var screen = Render(harness.Dialog);
            var keyword = Locate(screen, "return");
            var text = Locate(screen, "'hi'");
            var comment = Locate(screen, "# note");
            Assert.AreEqual(SyntaxPalette.Default.Keyword, screen.ForegroundAt(keyword.Column, keyword.Row), "the keyword is coloured as one");
            Assert.AreEqual(SyntaxPalette.Default.String, screen.ForegroundAt(text.Column, text.Row), "the string is coloured as one");
            Assert.AreEqual(SyntaxPalette.Default.Comment, screen.ForegroundAt(comment.Column, comment.Row), "the comment is coloured as one");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

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

        // Output, Errors, Analysis, then Debug: the Errors and Analysis tabs sit between Output and Debug.
        for (int i = 0; i < 3; i++) await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));

        CollectionAssert.Contains(harness.Dialog.OutputLines.ToList(), "Watch", "F7 three times moves the output panel to the debug tab");
        CollectionAssert.AreEqual(editorBefore, harness.Dialog.EditorLines.ToList(), "the editor keeps the source");

        // Debug, Coverage, Profile, then back to Output.
        for (int i = 0; i < 3; i++) await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));

        CollectionAssert.AreEqual(editorBefore, harness.Dialog.EditorLines.ToList(), "the editor keeps the source through the whole cycle");
        CollectionAssert.DoesNotContain(harness.Dialog.OutputLines.ToList(), "Watch", "six presses come back to the output log");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NRunsTheCodeAnalysisAndListsItsFindingsOnTheAnalysisTab()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllText(path, "one\ntwo\nthree\nfour\n");
        try
        {
            // The analyzers report the same warning once per target framework: it is listed once.
            var runner = new FakeProcessRunner
            {
                Output =
                [
                    $"{path}(3,5): warning CA1822: Member 'Run' does not access instance data [App.csproj]",
                    $"{path}(3,5): warning CA1822: Member 'Run' does not access instance data [App.csproj]",
                    $"{path}(4,1): error CS1002: ; expected [App.csproj]",
                ],
            };
            var settings = new FakeSettingsService();
            var harness = await OpenAsync([Solution, App], runner: runner, settings: settings);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.N, 'n'));
            await WaitIdleAsync(harness.Dialog);

            Assert.IsTrue(harness.Runner.Commands.Single().Arguments.Contains("--no-incremental"), "the analysis builds without the incremental cache, so the analyzers run");
            Assert.AreEqual(StudioPanelKind.Output, harness.Dialog.FocusedPanel, "the Analysis tab takes the focus");

            var lines = harness.Dialog.OutputLines.ToList();
            Assert.AreEqual(3, lines.Count, "the summary, then one row per finding");
            StringAssert.Contains(lines[0], "1 error(s), 1 warning(s)", "the summary counts the findings");
            CollectionAssert.Contains(lines, $"{path}(3,5): warning CA1822: Member 'Run' does not access instance data [App.csproj]");
            Assert.IsTrue(StudioSettings.DecodeDurations(settings.Get(StudioSettings.AnalysisDurationsKey)).ContainsKey(Solution.Path), "the time it took is kept as the next estimate");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAnAnalysisFindingOpensItsFileAtItsLine()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllText(path, "one\ntwo\nthree\nfour\n");
        try
        {
            var runner = new FakeProcessRunner { Output = [$"{path}(3,5): warning CA1822: Member 'Run' does not access instance data [App.csproj]"] };
            var harness = await OpenAsync([Solution, App], runner: runner);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.N, 'n'));
            await WaitIdleAsync(harness.Dialog);

            // Row 0 is the summary, so the finding is one Down away.
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.CursorLineNumber == 3, $"the editor to open the file at line 3 (opened: {harness.Dialog.CursorLineNumber}, status: {harness.Dialog.Status})");
            StringAssert.Contains(harness.Dialog.EditorLines[2], "three");
            Assert.AreEqual(StudioPanelKind.Editor, harness.Dialog.FocusedPanel, "the opened file takes the focus");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static DockSide SideOf(DevelopmentStudioDialog dialog, StudioPanelKind kind) =>
        dialog.Panels.Single(panel => panel.Kind == kind).Side;

    private static int PercentOf(DevelopmentStudioDialog dialog, StudioPanelKind kind) =>
        dialog.Panels.Single(panel => panel.Kind == kind).Percent;

    /// <summary>Tab (the Tab command's own move) until the focus is on the panel, or fails after the four panels.</summary>
    private static void FocusPanel(DevelopmentStudioDialog dialog, StudioPanelKind kind)
    {
        for (int i = 0; i < 4 && dialog.FocusedPanel != kind; i++)
        {
            dialog.OnHandleTabForward();
        }

        Assert.AreEqual(kind, dialog.FocusedPanel, $"Tab reaches the {kind} panel");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSolutionExplorerHasTheFocusWhenTheStudioOpens()
    {
        var harness = await OpenAsync([Solution, App]);

        Assert.AreEqual(StudioPanelKind.Explorer, harness.Dialog.FocusedPanel);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheLayoutSbomFormatAndOutputTabAreKeptForTheNextOpening()
    {
        var settings = new FakeSettingsService();
        var first = await OpenAsync([Solution, App], settings: settings);

        // F8 and Left move the focused Explorer to the empty left edge; F8 ends the layout mode.
        await first.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));
        await first.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        await first.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));
        await first.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));
        await first.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F, 'f'));

        var second = await OpenAsync([Solution, App], settings: settings);

        Assert.AreEqual(DockSide.Left, SideOf(second.Dialog, StudioPanelKind.Explorer), "the Explorer keeps the edge it was moved to");
        Assert.AreEqual(DockSide.Fill, SideOf(second.Dialog, StudioPanelKind.Editor), "the centre is still the editor");
        Assert.AreEqual("Errors", settings.Get(StudioSettings.OutputTabKey), "the output tab is saved");

        // The SBOM format was saved as JSON, so the next F switches it back to XML.
        Assert.AreEqual("Json", settings.Get(StudioSettings.SbomFormatKey));
        await second.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F, 'f'));
        Assert.AreEqual("Xml", settings.Get(StudioSettings.SbomFormatKey), "the SBOM format is restored on opening");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheExplorerExpansionAndCursorRowAreKeptForTheNextOpening()
    {
        var settings = new FakeSettingsService();
        var firstRunner = new FakeProcessRunner();
        var first = await OpenAsync([Solution, App, AppTest, Script, Java], runner: firstRunner, settings: settings);

        // Left collapses the solution under the cursor; Down moves onto the script below it.
        await first.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        await first.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        first.Dialog.OnIdle();

        Assert.AreEqual("/src/tool.py", settings.Get(StudioSettings.SelectedPathKey), "the cursor row is saved");
        Assert.AreEqual(0, StudioSettings.DecodeList(settings.Get(StudioSettings.ExpandedFoldersKey)).Count, "the collapsed solution is saved as collapsed");

        var secondRunner = new FakeProcessRunner();
        var second = await OpenAsync([Solution, App, AppTest, Script, Java], runner: secondRunner, settings: settings);

        CollectionAssert.Contains(second.Dialog.ExplorerRows.ToList(), "▸ Solution  App", "the solution stays collapsed");
        Assert.IsFalse(second.Dialog.ExplorerRows.Any(row => row.Contains("App.Test", StringComparison.Ordinal)), "its projects stay hidden");

        // B builds the item under the cursor, so the saved row is the one it builds.
        await second.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(second.Dialog);
        CollectionAssert.Contains(secondRunner.Commands.Single().Arguments.ToList(), "/src/tool.py", "the cursor is back on the script");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACommandsElapsedTimeIsWrittenToTheOutputAndTheStatus()
    {
        var runner = new FakeProcessRunner { Output = ["Build succeeded."] };
        var harness = await OpenAsync([Solution, App, AppTest], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        string elapsed = harness.Dialog.OutputLines.Single(line => line.StartsWith("Elapsed ", StringComparison.Ordinal));
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(elapsed, @"^Elapsed \d+:\d{2}$"), $"'{elapsed}' is the elapsed time");
        Assert.IsTrue(harness.Dialog.Status.EndsWith(elapsed, StringComparison.Ordinal), "the status line ends with the same elapsed time");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheErrorListCollectsTheErrorsAndWarningsOfACommandOnce()
    {
        var runner = new FakeProcessRunner
        {
            Output =
            [
                "/src/App/Program.cs(3,5): error CS1002: ; expected [/src/App/App.csproj]",
                "/src/App/Program.cs(5,9): warning CS0168: The variable 'x' is declared but never used [/src/App/App.csproj]",
                "Build FAILED.",
                "/src/App/Program.cs(3,5): error CS1002: ; expected [/src/App/App.csproj]",
                "    1 Error(s)",
            ],
        };
        var harness = await OpenAsync([Solution, App], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        CollectionAssert.AreEqual(new[]
        {
            "/src/App/Program.cs(3,5): error CS1002: ; expected [/src/App/App.csproj]",
            "/src/App/Program.cs(5,9): warning CS0168: The variable 'x' is declared but never used [/src/App/App.csproj]",
        }, harness.Dialog.ErrorLines.ToList(), "MSBuild prints the error again in its summary, and the list shows it once");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheErrorListStartsOverWithTheNextCommand()
    {
        var runner = new FakeProcessRunner { Output = ["/src/App/Program.cs(3,5): error CS1002: ; expected"] };
        var harness = await OpenAsync([Solution, App], runner: runner);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);
        Assert.AreEqual(1, harness.Dialog.ErrorLines.Count);

        runner.Output = ["Build succeeded."];
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        CollectionAssert.AreEqual(Array.Empty<string>(), harness.Dialog.ErrorLines.ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAnErrorOpensItsFileAtTheLine()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllText(path, "one\ntwo\nthree\nfour\n");
        try
        {
            var runner = new FakeProcessRunner { Output = [$"{path}(3,5): error CS1002: ; expected"] };
            var harness = await OpenAsync([Solution, App], runner: runner);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
            await WaitIdleAsync(harness.Dialog);

            // The Errors tab of the output panel: focus the panel, then F7 from Output to Errors.
            FocusPanel(harness.Dialog, StudioPanelKind.Output);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));
            Assert.AreEqual(1, harness.Dialog.ErrorLines.Count, "the build's error is listed for the Errors tab");
            CollectionAssert.Contains(harness.Dialog.OutputLines.ToList(), $"{path}(3,5): error CS1002: ; expected", "F7 shows the Errors tab in the output panel");
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.CursorLineNumber == 3, $"the editor to open the file at line 3 (opened: {harness.Dialog.CursorLineNumber}, focus: {harness.Dialog.FocusedPanel}, status: {harness.Dialog.Status})");
            StringAssert.Contains(harness.Dialog.EditorLines[2], "three");
            Assert.AreEqual(StudioPanelKind.Editor, harness.Dialog.FocusedPanel, "the opened file takes the focus");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileOpenedFromTheErrorListHasTheFocusAndTakesTyping()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllText(path, "one\ntwo\nthree\nfour\n");
        try
        {
            var runner = new FakeProcessRunner { Output = [$"{path}(3,5): error CS1002: ; expected"] };
            var harness = await OpenAsync([Solution, App], runner: runner);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
            await WaitIdleAsync(harness.Dialog);
            FocusPanel(harness.Dialog, StudioPanelKind.Output);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.CursorLineNumber == 3, "the editor to open the file at line 3");

            // No FocusPanel here: the key goes straight to the file the user just opened.
            Assert.AreEqual(StudioPanelKind.Editor, harness.Dialog.FocusedPanel, "the opened file has the focus without another key");
            await harness.Dialog.HandleKeyEventAsync(Char('X'));
            // The error names column 5, so the cursor is before the fifth character of "three".
            StringAssert.Contains(harness.Dialog.EditorLines[2], "threXe", "a key pressed right after opening goes into the file, at the column the error names");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F8StartsThePanelLayoutAndArrowsMoveTheFocusedPanelToAnEmptyEdge()
    {
        var harness = await OpenAsync([Solution, App]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));
        Assert.IsTrue(harness.Dialog.IsLayoutMode);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.AreEqual(DockSide.Left, SideOf(harness.Dialog, StudioPanelKind.Explorer));
        Assert.AreEqual(StudioPanelKind.Explorer, harness.Dialog.FocusedPanel, "the panel keeps the focus when it moves");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnArrowMovesThePanelOntoAnEmptyEdge()
    {
        var harness = await OpenAsync([Solution, App]);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));

        Assert.AreEqual(DockSide.Top, SideOf(harness.Dialog, StudioPanelKind.Explorer));
        Assert.AreEqual(DockSide.Bottom, SideOf(harness.Dialog, StudioPanelKind.Output), "the output panel keeps its edge");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheCentrePanelCannotLeaveTheCentreForAnEmptyEdge()
    {
        var harness = await OpenAsync([Solution, App]);
        FocusPanel(harness.Dialog, StudioPanelKind.Editor);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.AreEqual(DockSide.Fill, SideOf(harness.Dialog, StudioPanelKind.Editor));
        StringAssert.Contains(harness.Dialog.Status, "swap places");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftArrowsMoveTheInnerBorderOfAnEdgePanel()
    {
        var harness = await OpenAsync([Solution, App]);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow, shift: true));
        Assert.AreEqual(39, PercentOf(harness.Dialog, StudioPanelKind.Explorer), "the Explorer is on the right, so Shift+Left grows it");

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow, shift: true));
        Assert.AreEqual(39, PercentOf(harness.Dialog, StudioPanelKind.Explorer), "the arrow along the panel's edge does nothing");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftArrowsDoNotResizeTheCentrePanel()
    {
        var harness = await OpenAsync([Solution, App]);
        FocusPanel(harness.Dialog, StudioPanelKind.Editor);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow, shift: true));

        StringAssert.Contains(harness.Dialog.Status, "fills what the edges leave");
        Assert.AreEqual(DockSide.Fill, SideOf(harness.Dialog, StudioPanelKind.Editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscEndsThePanelLayoutWithoutClosingTheDialog()
    {
        var harness = await OpenAsync([Solution, App]);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F8));

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(harness.Dialog.IsLayoutMode);
        Assert.IsTrue(harness.Dialog.IsVisible, "Esc ends the layout mode, and the studio stays open");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscDoesNotCloseTheStudio()
    {
        var harness = await OpenAsync([Solution, App]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(harness.Dialog.IsVisible, "Esc does not close the studio");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlWClosesTheStudio()
    {
        var harness = await OpenAsync([Solution, App]);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.W, 'w', control: true));

        Assert.IsFalse(harness.Dialog.IsVisible, "Ctrl+W closes the studio");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHotKeyBarListsTheCommandsOfThePanelThatHasTheFocus()
    {
        var harness = await OpenAsync([Solution, App]);

        // The explorer has the focus when the studio opens: its commands include the ones only it takes.
        var explorer = harness.Dialog.HotKeyCommands;
        CollectionAssert.Contains(explorer.ToList(), "Enter:open, run a test or expand");
        CollectionAssert.Contains(explorer.ToList(), "U:NuGet");
        CollectionAssert.Contains(explorer.ToList(), "Ctrl+,:settings");

        // The Debug tab of the output panel lists the debugger's keys, and the explorer's do not appear there.
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);
        for (int press = 0; press < 3; press++) await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));

        Assert.AreEqual(StudioPanelKind.Output, harness.Dialog.FocusedPanel, "the output panel has the focus after a command");
        var debug = harness.Dialog.HotKeyCommands.ToList();
        CollectionAssert.Contains(debug, "W:watch the word under the cursor");
        CollectionAssert.Contains(debug, "Shift+F5:stop");
        CollectionAssert.DoesNotContain(debug, "Enter:open, run a test or expand");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheHotKeyBarIsDrawnBelowThePanels()
    {
        var harness = await OpenAsync([Solution, App]);

        var screen = Render(harness.Dialog);

        AssertScreenContains(screen, "U:NuGet", "the explorer's commands are drawn in the hot key bar");
        AssertScreenContains(screen, "Ctrl+W:close");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheErrorsAreAnOutputTabNextToOutput()
    {
        var runner = new FakeProcessRunner { Output = ["/src/App/Program.cs(3,5): error CS1002: ; expected"] };
        var harness = await OpenAsync([Solution, App], runner: runner);
        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));

        var screen = Render(harness.Dialog);
        AssertScreenContains(screen, "Errors");
        AssertScreenContains(screen, "Program.cs(3,5): error CS1002");
        Assert.AreEqual(1, harness.Dialog.ErrorLines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingInTheEditorEditsTheFileAndCtrlSSavesIt()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "Program.cs");
        File.WriteAllText(path, "one\ntwo\nthree\nfour\n");
        try
        {
            var runner = new FakeProcessRunner { Output = [$"{path}(3,5): error CS1002: ; expected"] };
            var harness = await OpenAsync([Solution, App], runner: runner);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
            await WaitIdleAsync(harness.Dialog);
            FocusPanel(harness.Dialog, StudioPanelKind.Output);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.CursorLineNumber == 3, "the editor to open the file at line 3");

            FocusPanel(harness.Dialog, StudioPanelKind.Editor);
            await harness.Dialog.HandleKeyEventAsync(Char('X'));
            StringAssert.Contains(harness.Dialog.EditorLines[2], "threXe", "a typed letter goes into the file, not to a command");

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.S, 's', control: true));
            await WaitUntilAsync(() => File.ReadAllText(path).Contains("threXe", StringComparison.Ordinal), "Ctrl+S to write the file");
            StringAssert.Contains(File.ReadAllText(path), "threXe");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFooterNamesTheKeysOfThePanelThatHasTheFocus()
    {
        var harness = await OpenAsync([Solution, App]);

        FocusPanel(harness.Dialog, StudioPanelKind.Output);
        AssertScreenContains(Render(harness.Dialog), "F7:output tab");

        FocusPanel(harness.Dialog, StudioPanelKind.Explorer);
        AssertScreenContains(Render(harness.Dialog), "Left/Right:collapse or expand");
    }
    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningAFileMovesTheFocusToTheEditor()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "tool.py");
        File.WriteAllText(path, "print('hi')\n");
        try
        {
            var script = new DevelopmentProject("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, path);
            var harness = await OpenAsync([script]);
            Assert.AreEqual(StudioPanelKind.Explorer, harness.Dialog.FocusedPanel);

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

            Assert.AreEqual(StudioPanelKind.Editor, harness.Dialog.FocusedPanel, "the editor has the focus as soon as the file is opened");
            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.EditorLines.Contains("print('hi')"), "the file to open");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheEditorTakesTheCursorKeysPunctuationAndUndo()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"DevStudio_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "tool.py");
        File.WriteAllText(path, "one\ntwo\nthree\n");
        try
        {
            var script = new DevelopmentProject("tool.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, path);
            var harness = await OpenAsync([script]);
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));
            await WaitUntilOpenAsync(harness.Dialog, () => harness.Dialog.EditorLines.Contains("three"), "the file to open");

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
            Assert.AreEqual(3, harness.Dialog.CursorLineNumber, "the arrows move the cursor through the file");

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.End));
            await harness.Dialog.HandleKeyEventAsync(Char('('));
            Assert.AreEqual("three()", harness.Dialog.EditorLines[2], "a punctuation mark is typed, and a bracket is closed as in the other editors");

            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Z, 'z', control: true));
            Assert.AreEqual("three", harness.Dialog.EditorLines[2], "Ctrl+Z takes the typing back");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARunSelectsTheOutputTabWithTheBuildResultsAndTakesTheFocus()
    {
        var runner = new FakeProcessRunner { Output = ["Build succeeded."] };
        var harness = await OpenAsync([Solution, App], runner: runner);

        await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        await WaitIdleAsync(harness.Dialog);

        Assert.AreEqual(StudioPanelKind.Output, harness.Dialog.FocusedPanel, "the output panel has the focus after a build");
        Assert.IsTrue(harness.Dialog.OutputLines.Any(line => line.StartsWith("$ ", StringComparison.Ordinal)), "the Output tab lists the command that built");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheExplorerScrollsWithTheCursorAndShowsAScrollbar()
    {
        var scripts = Enumerable.Range(0, 60)
            .Select(i => new DevelopmentProject($"tool{i:00}.py", DevelopmentLanguage.Python, DevelopmentProjectKind.Script, $"/src/tool{i:00}.py"))
            .ToList();
        var harness = await OpenAsync(scripts);

        for (int i = 0; i < 45; i++)
        {
            await harness.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        }

        var screen = Render(harness.Dialog);
        AssertScreenContains(screen, "tool45.py", "the cursor row is scrolled into view");
        var rowsOnScreen = Enumerable.Range(0, (int)iTerminal.Core.Test.Helper.UiTestHelper.ScreenSize.Height).Select(row => screen.Line(row));
        Assert.IsFalse(rowsOnScreen.Any(line => line.Contains("tool00.py", StringComparison.Ordinal)), "the rows above the cursor have scrolled out of the list");
        AssertScreenContains(screen, Border.ArrowUp.ToString(), "the explorer draws its scrollbar");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheMenuBarTitlesLeadTheMenusInOrder()
    {
        var harness = await OpenAsync([Solution, App]);

        CollectionAssert.AreEqual(new[] { "File", "Build", "Debug", "View", "Tools" }, harness.Dialog.MenuTitles.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EveryHotKeyOfTheStudioIsListedInAMenu()
    {
        var harness = await OpenAsync([Solution, App]);
        var listed = Enum.GetValues<DevelopmentStudioDialog.MenuBarTitle>()
            .SelectMany(title => harness.Dialog.MenuItemsOf(title))
            .Select(item => item.Shortcut)
            .ToHashSet();

        string[] hotKeys =
        [
            "Enter", "Ctrl+S", "Ctrl+R", "Ctrl+,", "Ctrl+W", "Ctrl+F5", "B", "R", "T", "C", "X", "Space", "A", "Shift+T", "D", "V", "K", "U",
            "F5", "Shift+F5", "F9", "F10", "F11", "Shift+F11", "W", "Delete", "M", "F6", "F7", "F8", "Right", "Left", "G", "N", "O", "P", "S", "F",
        ];
        CollectionAssert.IsSubsetOf(hotKeys, listed.ToArray(), "every key the studio handles is also an entry of a menu");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMenuEntryRunsTheCommandItsHotKeyRuns()
    {
        var runner = new FakeProcessRunner();
        var harness = await OpenAsync([Solution, App], runner: runner);
        var build = harness.Dialog.MenuItemsOf(DevelopmentStudioDialog.MenuBarTitle.Build).Single(item => item.Shortcut == "B");

        await build.Execute!();
        await WaitIdleAsync(harness.Dialog);

        Assert.AreEqual(1, runner.Commands.Count, "the Build entry starts the build, as B does");
    }
}
