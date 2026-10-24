using iConsole.Data;
using iConsole.Diagnostics;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Diagnostics;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// Full-stack integration test that wires both file panels exactly as the real application
/// does (via FilePanelFactory and real services), drives them with key events, and asserts
/// real-time performance budgets for init, navigation, and prefetch.
///
/// Content-load budgets are measured via a <see cref="RecordingStopwatchFactory"/> injected
/// into <see cref="ContentModelService"/>, which flows down to each
/// <see cref="iTerminal.Core.Tasks.FilePanelRefreshTask"/> and populates its
/// <c>PhaseDurations</c>. Asserting on those internal phase durations — rather than wrapping
/// the whole navigate-and-wait sequence in an external Stopwatch — keeps the assertions
/// immune to TaskManager scheduling jitter under parallel test-class contention (same
/// approach as FilePanelRefreshTaskTimingTest).
/// </summary>
// Wall-clock timing assertions: run isolated from other classes (which run in parallel per the
// assembly-level Parallelize setting) so CPU/thread-pool contention doesn't inflate measurements.
[TestClass]
[DoNotParallelize]
public class FilePanelIntegrationTest
{
    // Performance budgets — generous to stay green on slow CI agents
    private const int KeyHandlerBudgetMs = 200;  // HandleKeyEventAsync must return before background work
    private const int ContentLoadBudgetMs = 3000; // Internal Enumeration+UiUpdate phases must complete within this window

    // ────────────────────────────────────────────────────────────────────────────────
    // Timing collection — flushed to console by TestCleanup so each test is legible
    // ────────────────────────────────────────────────────────────────────────────────

    private readonly List<(string Label, long Ms)> _timings = [];

    private void Record(string label, long ms) => _timings.Add((label, ms));

    [TestCleanup]
    public void PrintTimings()
    {
        if (_timings.Count == 0) return;
        Console.WriteLine("── Integration test timings ─────────────────────────────────────");
        foreach (var (label, ms) in _timings)
        {
            Console.WriteLine($"  {label,-58} {ms,5} ms");
        }
        Console.WriteLine("─────────────────────────────────────────────────────────────────");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // App harness — mirrors the real DI composition root without any database or terminal
    // ────────────────────────────────────────────────────────────────────────────────

    private sealed class AppHarness : IAsyncDisposable
    {
        public string LeftRoot { get; }
        public string RightRoot { get; }

        public ContentModelService ContentModelService { get; }
        public FilePanel LeftPanel { get; }
        public FilePanel RightPanel { get; }

        // ── Debugging infrastructure: every command run by either panel and every rendered
        // frame is captured here, so agentic/integration tests can assert on what the app did
        // and what it displayed, not just on model state. ─────────────────────────────────────
        public InMemoryCommandLogger CommandLogger { get; } = new();
        public InMemoryScreenLogger ScreenLogger { get; } = new();
        public MockBuffer LeftBuffer { get; }
        public MockBuffer RightBuffer { get; }

        private readonly string _tempRoot;

        private AppHarness(
            string tempRoot, string leftRoot, string rightRoot,
            ContentModelService contentModelService,
            FilePanel leftPanel, FilePanel rightPanel)
        {
            _tempRoot = tempRoot;
            LeftRoot = leftRoot;
            RightRoot = rightRoot;
            ContentModelService = contentModelService;
            LeftPanel = leftPanel;
            RightPanel = rightPanel;
            LeftBuffer = new MockBuffer(leftPanel.ViewSize);
            RightBuffer = new MockBuffer(rightPanel.ViewSize);

            leftPanel.SetCommandLogger(CommandLogger);
            rightPanel.SetCommandLogger(CommandLogger);
        }

        /// <summary>
        /// Forces a full redraw of <paramref name="panel"/> into its mock buffer and records the
        /// resulting text in <see cref="ScreenLogger"/> under <paramref name="label"/>, so a test
        /// can assert on what the screen looked like at that point (e.g. after a key event).
        /// </summary>
        public void CaptureScreen(FilePanel panel, string label)
        {
            var buffer = ReferenceEquals(panel, LeftPanel) ? LeftBuffer : RightBuffer;
            panel.Refresh();
            panel.Render(buffer);
            buffer.Merge();
            ScreenLogger.Capture(label, buffer.MergedLayer);
        }

        public static AppHarness Create()
        {
            // ── Temporary filesystem hierarchy ───────────────────────────────────
            var tempRoot = Path.Combine(Path.GetTempPath(), "FilePanelIntTest_" + Guid.NewGuid().ToString("N")[..8]);
            var leftRoot = Path.Combine(tempRoot, "left");
            var rightRoot = Path.Combine(tempRoot, "right");

            Directory.CreateDirectory(Path.Combine(leftRoot, "alpha"));
            File.WriteAllText(Path.Combine(leftRoot, "alpha", "file_in_alpha.txt"), "a");
            Directory.CreateDirectory(Path.Combine(leftRoot, "beta"));
            File.WriteAllText(Path.Combine(leftRoot, "beta", "file_in_beta.txt"), "b");
            File.WriteAllText(Path.Combine(leftRoot, "gamma.txt"), "g");

            Directory.CreateDirectory(Path.Combine(rightRoot, "x_dir"));
            File.WriteAllText(Path.Combine(rightRoot, "x_dir", "file_in_x.txt"), "x");
            Directory.CreateDirectory(Path.Combine(rightRoot, "y_dir"));
            File.WriteAllText(Path.Combine(rightRoot, "zeta.txt"), "z");

            // ── Service stack (real implementations; only irrelevant branches mocked) ──
            var taskManager = new TaskManager();
            var memoryCacheService = new MemoryCacheService();
            var settingsService = new SettingsService();          // in-memory temp SQLite
            var folderHistory = new FolderHistoryService(settingsService);
            var frequentGuard = new FrequentExecutionGuardService(new DateTimeProviderService());
            var fileSystemService = new FileSystemService();
            var logger = Serilog.Core.Logger.None;
            var themeService = new ThemeService();

            // Seed initial paths so OnInitializeAsync reads them from settings
            settingsService.Set(ContentModelService.Left, "file://" + leftRoot);
            settingsService.Set(ContentModelService.Right, "file://" + rightRoot);

            var contentModelService = new ContentModelService(
                taskManager,
                settingsService,
                fileSystemService,
                Mock.Of<IMemoryDriveService>(),
                Mock.Of<ISshFileSystemService>(),
                Mock.Of<ISmbFileSystemService>(),
                Mock.Of<IIPhoneFileSystemService>(),
                Mock.Of<INetworkProfileService>(),
                Mock.Of<IPasswordManager>(),
                Mock.Of<IArchiveFileSystemService>(),
                folderHistory,
                frequentGuard,
                memoryCacheService,
                logger,
                stopwatchFactory: new RecordingStopwatchFactory());

            var terminalModel = new TerminalModel(taskManager, contentModelService);

            var factory = new FilePanelFactory(
                taskManager, contentModelService, terminalModel,
                memoryCacheService, settingsService, logger, themeService);

            // Set ViewSize before init so ContentModel.ViewSize is non-zero (required by scroll commands)
            var leftPanel = factory.Create(ContentModelService.Left);
            leftPanel.ViewSize = new Size(80, 24);

            var rightPanel = factory.Create(ContentModelService.Right);
            rightPanel.ViewSize = new Size(80, 24);

            return new AppHarness(tempRoot, leftRoot, rightRoot, contentModelService, leftPanel, rightPanel);
        }

        public ContentModelFileSystem? LeftModel =>
            ContentModelService.GetModel(ContentModelService.Left) as ContentModelFileSystem;

        public ContentModelFileSystem? RightModel =>
            ContentModelService.GetModel(ContentModelService.Right) as ContentModelFileSystem;

        public async ValueTask DisposeAsync()
        {
            await Task.Run(() => { try { Directory.Delete(_tempRoot, recursive: true); } catch { } });
        }
    }

    // ────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Polls until the named entry appears in the panel's visible entry list.</summary>
    private static async Task WaitForEntryAsync(
        ContentModelFileSystem model, string entryName, int timeoutMs = ContentLoadBudgetMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (model.FilteredEntries.Any(e => e.Url.DisplayName == entryName))
                return;
            await Task.Delay(20);
        }
        var found = model.FilteredEntries.Select(e => e.Url.DisplayName).ToList();
        Assert.Fail($"Entry '{entryName}' did not appear within {timeoutMs} ms. " +
                    $"Current entries: [{string.Join(", ", found)}]");
    }

    /// <summary>
    /// Polls until <see cref="ContentModelFileSystem.LastRefreshTask"/> has finished and
    /// populated its PhaseDurations, then returns the total of the "Enumeration" and (if
    /// present) "UiUpdate" phases — the actual internal cost of loading the folder's
    /// contents, independent of how long the task waited in the TaskManager queue.
    /// </summary>
    private static async Task<double> WaitForContentLoadPhaseMsAsync(
        ContentModelFileSystem model, int timeoutMs = ContentLoadBudgetMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (model.LastRefreshTask?.PhaseDurations is { } phases && phases.ContainsKey("Enumeration"))
            {
                var ms = phases["Enumeration"].TotalMilliseconds;
                if (phases.TryGetValue("UiUpdate", out var uiUpdate))
                    ms += uiUpdate.TotalMilliseconds;
                return ms;
            }
            await Task.Delay(20);
        }
        Assert.Fail($"PhaseDurations for {model.Url.FullUrl} did not populate within {timeoutMs} ms.");
        return default; // unreachable
    }

    private static KeyEvent Key(ConsoleKey key, bool ctrl = false, bool shift = false, bool alt = false) =>
        new KeyEvent('\0', key, shift: shift, alt: alt, control: ctrl);

    // ────────────────────────────────────────────────────────────────────────────────
    // Tests
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Both panels initialise and populate their entry lists within the performance budget.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task FullApplication_Init_BothPanelsLoadEntriesWithinBudget()
    {
        await using var app = AppHarness.Create();

        // Left panel
        var sw = Stopwatch.StartNew();
        await app.LeftPanel.OnInitializeAsync();
        Record("LeftPanel.OnInitializeAsync() returns", sw.ElapsedMilliseconds);

        await WaitForEntryAsync(app.LeftModel!, "alpha");
        var leftLoadMs = await WaitForContentLoadPhaseMsAsync(app.LeftModel!);
        Record("LeftPanel content-load phase (Enumeration+UiUpdate)", (long)leftLoadMs);
        Assert.IsTrue(leftLoadMs <= ContentLoadBudgetMs,
            $"Left panel content-load phase took {leftLoadMs:F1} ms (budget {ContentLoadBudgetMs} ms)");

        // Right panel
        sw.Restart();
        await app.RightPanel.OnInitializeAsync();
        Record("RightPanel.OnInitializeAsync() returns", sw.ElapsedMilliseconds);

        await WaitForEntryAsync(app.RightModel!, "x_dir");
        var rightLoadMs = await WaitForContentLoadPhaseMsAsync(app.RightModel!);
        Record("RightPanel content-load phase (Enumeration+UiUpdate)", (long)rightLoadMs);
        Assert.IsTrue(rightLoadMs <= ContentLoadBudgetMs,
            $"Right panel content-load phase took {rightLoadMs:F1} ms (budget {ContentLoadBudgetMs} ms)");

        // Sanity: both panels show their own roots, not each other's content
        var leftNames = app.LeftModel!.FilteredEntries.Select(e => e.Url.DisplayName).ToList();
        var rightNames = app.RightModel!.FilteredEntries.Select(e => e.Url.DisplayName).ToList();
        Assert.IsTrue(leftNames.Contains("alpha"));
        Assert.IsTrue(leftNames.Contains("beta"));
        Assert.IsFalse(leftNames.Contains("x_dir"), "Left panel must not show right-panel content");
        Assert.IsTrue(rightNames.Contains("x_dir"));
        Assert.IsFalse(rightNames.Contains("alpha"), "Right panel must not show left-panel content");
    }

    /// <summary>
    /// HandleKeyEventAsync returns quickly (synchronous enqueue path) and entries load within budget.
    /// Exercises the full key-event → ContentModelService → ContentModelFileSystem → TaskManager chain.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task FullApplication_Navigate_KeyHandlerReturnsInstantlyEntriesLoadWithinBudget()
    {
        await using var app = AppHarness.Create();
        await app.LeftPanel.OnInitializeAsync();
        await WaitForEntryAsync(app.LeftModel!, "alpha");  // ensure entries are loaded before interacting

        // ── DownArrow: move cursor from ".." (index 0) to "alpha" (index 1) ─────
        var sw = Stopwatch.StartNew();
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Record("DownArrow handler returns", sw.ElapsedMilliseconds);
        Assert.IsTrue(sw.ElapsedMilliseconds <= KeyHandlerBudgetMs,
            $"DownArrow took {sw.ElapsedMilliseconds} ms to return (budget {KeyHandlerBudgetMs} ms)");

        Assert.AreEqual("alpha", app.LeftModel!.GetFocusedEntry()?.Url.DisplayName,
            "Cursor should be on 'alpha' after one DownArrow press");

        // ── Enter: navigate into alpha/ ───────────────────────────────────────────
        sw.Restart();
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        var keyHandlerMs = sw.ElapsedMilliseconds;
        Record("Enter (navigate to subfolder) handler returns", keyHandlerMs);
        Assert.IsTrue(keyHandlerMs <= KeyHandlerBudgetMs,
            $"Enter handler took {keyHandlerMs} ms to return (budget {KeyHandlerBudgetMs} ms)");

        await WaitForEntryAsync(app.LeftModel!, "file_in_alpha.txt");
        var alphaLoadMs = await WaitForContentLoadPhaseMsAsync(app.LeftModel!);
        Record("Entries appear in alpha/ after Enter (content-load phase)", (long)alphaLoadMs);
        Assert.IsTrue(alphaLoadMs <= ContentLoadBudgetMs,
            $"Navigation to alpha/ took {alphaLoadMs:F1} ms (budget {ContentLoadBudgetMs} ms)");

        Assert.IsTrue(
            app.LeftModel!.Url.Path.TrimEnd(Path.DirectorySeparatorChar)
               .EndsWith("alpha", StringComparison.OrdinalIgnoreCase),
            $"Expected to be inside alpha/, but Url is: {app.LeftModel.Url.FullUrl}");

        // ── Ctrl+PageUp: navigate back to left/ ──────────────────────────────────
        sw.Restart();
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.PageUp, ctrl: true));
        keyHandlerMs = sw.ElapsedMilliseconds;
        Record("Ctrl+PageUp (navigate to parent) handler returns", keyHandlerMs);
        Assert.IsTrue(keyHandlerMs <= KeyHandlerBudgetMs,
            $"Ctrl+PageUp took {keyHandlerMs} ms to return (budget {KeyHandlerBudgetMs} ms)");

        await WaitForEntryAsync(app.LeftModel!, "beta");
        var backLoadMs = await WaitForContentLoadPhaseMsAsync(app.LeftModel!);
        Record("Entries appear in left/ after Ctrl+PageUp (content-load phase)", (long)backLoadMs);
        Assert.IsTrue(backLoadMs <= ContentLoadBudgetMs,
            $"Navigation back to left/ took {backLoadMs:F1} ms (budget {ContentLoadBudgetMs} ms)");

        Assert.IsTrue(
            app.LeftModel!.Url.Path.TrimEnd(Path.DirectorySeparatorChar)
               .EndsWith("left", StringComparison.OrdinalIgnoreCase),
            $"Expected to be back in left/, but Url is: {app.LeftModel.Url.FullUrl}");
    }

    /// <summary>
    /// Prefetch tasks warm child-folder caches in the background; subsequent navigation into
    /// a pre-fetched subfolder resolves at least as fast as cold navigation.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task FullApplication_Prefetch_WarmCacheDoesNotExceedColdNavigationBudget()
    {
        await using var app = AppHarness.Create();
        await app.LeftPanel.OnInitializeAsync();
        await WaitForEntryAsync(app.LeftModel!, "alpha");

        // Allow prefetch tasks to warm child caches before we navigate
        await Task.Delay(600);

        // Navigate into alpha/ — prefetch may have already cached the listing
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)); // cursor → alpha (index 1)
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitForEntryAsync(app.LeftModel!, "file_in_alpha.txt");
        var alphaLoadMs = await WaitForContentLoadPhaseMsAsync(app.LeftModel!);
        Record("Navigate into prefetch-warmed alpha/ (content-load phase)", (long)alphaLoadMs);
        Assert.IsTrue(alphaLoadMs <= ContentLoadBudgetMs,
            $"Prefetch-warmed alpha/ navigation took {alphaLoadMs:F1} ms (budget {ContentLoadBudgetMs} ms)");

        // Navigate back, then into beta/ (also pre-fetched)
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.PageUp, ctrl: true));
        await WaitForEntryAsync(app.LeftModel!, "beta");

        // After navigate-back, RestoreFocusedEntry may leave cursor on the last-visited child
        // (alpha, index 1). Press Home to anchor to '..' (index 0), then move to beta (index 2).
        // Walk down to beta by name: where the cursor lands after navigating back is not what this test measures.
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.Home));
        for (var step = 0; step < 10 && app.LeftModel!.GetFocusedEntry()?.Url.DisplayName != "beta"; step++)
        {
            await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        }
        Assert.AreEqual("beta", app.LeftModel!.GetFocusedEntry()?.Url.DisplayName,
            "Cursor should be on 'beta'");

        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitForEntryAsync(app.LeftModel!, "file_in_beta.txt");
        var betaLoadMs = await WaitForContentLoadPhaseMsAsync(app.LeftModel!);
        Record("Navigate into prefetch-warmed beta/ (content-load phase)", (long)betaLoadMs);
        Assert.IsTrue(betaLoadMs <= ContentLoadBudgetMs,
            $"Prefetch-warmed beta/ navigation took {betaLoadMs:F1} ms (budget {ContentLoadBudgetMs} ms)");

        Assert.IsTrue(
            app.LeftModel!.Url.Path.TrimEnd(Path.DirectorySeparatorChar)
               .EndsWith("beta", StringComparison.OrdinalIgnoreCase),
            $"Expected to be inside beta/, but Url is: {app.LeftModel.Url.FullUrl}");
    }

    /// <summary>
    /// Left and right panels navigate independently without interfering with each other.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task FullApplication_DualPanel_BothPanelsOperateIndependently()
    {
        await using var app = AppHarness.Create();
        await app.LeftPanel.OnInitializeAsync();
        await app.RightPanel.OnInitializeAsync();
        await WaitForEntryAsync(app.LeftModel!, "alpha");
        await WaitForEntryAsync(app.RightModel!, "x_dir");

        // Navigate left panel into alpha/
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitForEntryAsync(app.LeftModel!, "file_in_alpha.txt");

        // Navigate right panel into x_dir/ independently
        await app.RightPanel.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await app.RightPanel.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitForEntryAsync(app.RightModel!, "file_in_x.txt");

        // Assert panels are in different locations
        Assert.IsTrue(
            app.LeftModel!.Url.Path.TrimEnd(Path.DirectorySeparatorChar)
               .EndsWith("alpha", StringComparison.OrdinalIgnoreCase),
            $"LeftPanel should be in alpha/ but is at: {app.LeftModel.Url.FullUrl}");
        Assert.IsTrue(
            app.RightModel!.Url.Path.TrimEnd(Path.DirectorySeparatorChar)
               .EndsWith("x_dir", StringComparison.OrdinalIgnoreCase),
            $"RightPanel should be in x_dir/ but is at: {app.RightModel.Url.FullUrl}");

        // Assert each panel shows only its own content
        var leftEntries = app.LeftModel!.FilteredEntries.Select(e => e.Url.DisplayName).ToList();
        var rightEntries = app.RightModel!.FilteredEntries.Select(e => e.Url.DisplayName).ToList();

        Assert.IsTrue(leftEntries.Contains("file_in_alpha.txt"),
            "Left panel must show file_in_alpha.txt");
        Assert.IsFalse(leftEntries.Contains("file_in_x.txt"),
            $"Left panel must NOT show right-panel content. Entries: [{string.Join(", ", leftEntries)}]");

        Assert.IsTrue(rightEntries.Contains("file_in_x.txt"),
            "Right panel must show file_in_x.txt");
        Assert.IsFalse(rightEntries.Contains("file_in_alpha.txt"),
            $"Right panel must NOT show left-panel content. Entries: [{string.Join(", ", rightEntries)}]");
    }

    /// <summary>
    /// Rapid successive SetUrl calls — simulating quick navigation — always deliver the final
    /// URL's content and never leave stale entries from intermediate folders.
    /// This tests the _prefetchScheduleCts race-condition fix in ContentModelFileSystem.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task FullApplication_RapidSetUrl_AlwaysDeliversFinalFolderWithNoStaleEntries()
    {
        await using var app = AppHarness.Create();
        await app.LeftPanel.OnInitializeAsync();

        var leftUrl = new Url("file://" + app.LeftRoot);
        var alphaUrl = new Url("file://" + Path.Combine(app.LeftRoot, "alpha") + Path.DirectorySeparatorChar);
        var betaUrl = new Url("file://" + Path.Combine(app.LeftRoot, "beta") + Path.DirectorySeparatorChar);

        // Three rapid navigations without waiting for results — the key race-condition scenario
        var sw = Stopwatch.StartNew();
        app.LeftModel!.SetUrl(alphaUrl);  // navigate to alpha/
        app.LeftModel!.SetUrl(betaUrl);   // immediately redirect to beta/
        app.LeftModel!.SetUrl(alphaUrl);  // immediately redirect back to alpha/
        Record("Three rapid SetUrl calls (all synchronous)", sw.ElapsedMilliseconds);
        Assert.IsTrue(sw.ElapsedMilliseconds <= KeyHandlerBudgetMs,
            $"Rapid SetUrl calls took {sw.ElapsedMilliseconds} ms (budget {KeyHandlerBudgetMs} ms)");

        sw.Restart();
        await WaitForEntryAsync(app.LeftModel!, "file_in_alpha.txt");
        Record("Final entries appear in alpha/ after rapid navigation", sw.ElapsedMilliseconds);

        // The final URL must be alpha/
        Assert.IsTrue(
            app.LeftModel.Url.Path.TrimEnd(Path.DirectorySeparatorChar)
               .EndsWith("alpha", StringComparison.OrdinalIgnoreCase),
            $"Should be in alpha/ after rapid navigation, but Url is: {app.LeftModel.Url.FullUrl}");

        // Stale entries from intermediate folders (beta/ and left/) must NOT appear
        var names = app.LeftModel.FilteredEntries.Select(e => e.Url.DisplayName).ToList();
        Assert.IsFalse(names.Contains("beta"),
            $"Stale 'beta' appeared in alpha/: [{string.Join(", ", names)}]");
        Assert.IsFalse(names.Contains("gamma.txt"),
            $"Stale 'gamma.txt' appeared in alpha/: [{string.Join(", ", names)}]");
        Assert.IsFalse(names.Contains("file_in_beta.txt"),
            $"Stale 'file_in_beta.txt' appeared in alpha/: [{string.Join(", ", names)}]");
    }

    /// <summary>
    /// Demonstrates the Command/Screen debugging infrastructure: every command run while
    /// driving the app via key events is captured by <see cref="InMemoryCommandLogger"/>, and
    /// every rendered frame can be captured as text by <see cref="InMemoryScreenLogger"/>. This
    /// lets an agentic test validate not just model state, but exactly what the app *did* and
    /// what it *displayed* at each step.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task FullApplication_DebuggingInfrastructure_CommandAndScreenLoggersCaptureNavigation()
    {
        await using var app = AppHarness.Create();
        await app.LeftPanel.OnInitializeAsync();
        await WaitForEntryAsync(app.LeftModel!, "alpha");

        // ── Screen logger: render the initial state and check the entries are visible ──────
        app.CaptureScreen(app.LeftPanel, "after-init");
        var initialScreen = app.ScreenLogger.Last;
        Assert.IsNotNull(initialScreen, "Expected a screen snapshot after CaptureScreen");
        Assert.IsTrue(initialScreen!.Text.Contains("alpha"),
            $"Expected initial screen to show 'alpha'. Screen was:{Environment.NewLine}{initialScreen.Text}");

        // ── Command logger: driving a key event must record at least one executed command ──
        app.CommandLogger.Clear();
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.IsTrue(app.CommandLogger.Records.Count > 0,
            "Expected DownArrow to run at least one command, but CommandLogger captured none");
        Assert.IsTrue(app.CommandLogger.Records.Any(r => r.Executed),
            "Expected at least one command to report Executed=true");
        Assert.IsTrue(app.CommandLogger.Records.All(r => r.ExceptionMessage is null),
            $"No command should throw. Exceptions: [{string.Join(", ", app.CommandLogger.Records.Select(r => r.ExceptionMessage))}]");

        // ── Navigate into alpha/ and confirm both loggers reflect the new state ─────────────
        await app.LeftPanel.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await WaitForEntryAsync(app.LeftModel!, "file_in_alpha.txt");

        app.CaptureScreen(app.LeftPanel, "after-enter-alpha");
        var afterEnterScreen = app.ScreenLogger.Last;
        Assert.IsTrue(afterEnterScreen!.Text.Contains("file_in_alpha.txt"),
            $"Expected screen after Enter to show 'file_in_alpha.txt'. Screen was:{Environment.NewLine}{afterEnterScreen.Text}");

        Assert.IsTrue(app.CommandLogger.Records.Any(r => r.Executed),
            "Expected the Enter key to have run an executed command");
    }
}
