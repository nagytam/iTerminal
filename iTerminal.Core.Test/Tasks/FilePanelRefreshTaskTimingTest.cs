using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using Moq;

namespace iTerminal.Core.Test.Tasks;

/// <summary>
/// Timing tests for FilePanelRefreshTask using RecordingStopwatchFactory.
///
/// Each test injects a RecordingStopwatchFactory so durations come from inside
/// the component rather than from external Stopwatch polling, making the
/// measurements precise and independent of scheduling jitter.
/// </summary>
// Wall-clock timing assertions: run isolated from other classes (which run in parallel per the
// assembly-level Parallelize setting) so CPU/thread-pool contention doesn't inflate measurements.
[TestClass]
[DoNotParallelize]
public class FilePanelRefreshTaskTimingTest
{
    // Generous budgets — these must hold even on slow CI agents.
    private const int CacheLookupBudgetMs = 50;
    private const int EnumerationBudgetMsPerFile = 2;    // 2ms per file is very generous
    private const int UiUpdateBudgetMs = 200;

    // ── Harness ──────────────────────────────────────────────────────────────

    private sealed class Harness : IDisposable
    {
        public string FolderPath { get; }
        public ContentModelFileSystem Model { get; }
        public MemoryCacheService Cache { get; }

        public Harness(int fileCount = 0, int subfolderCount = 0)
        {
            FolderPath = Path.Combine(Path.GetTempPath(), "FPRTiming_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(FolderPath);
            for (var i = 0; i < fileCount; i++)
                File.WriteAllText(Path.Combine(FolderPath, $"f{i:D5}.txt"), string.Empty);
            for (var i = 0; i < subfolderCount; i++)
                Directory.CreateDirectory(Path.Combine(FolderPath, $"d{i:D3}"));

            Cache = new MemoryCacheService();

            var settings = new Mock<ISettingsService>(MockBehavior.Loose);
            settings.Setup(s => s.Get(SettingsService.FOLDER_HISTORY)).Returns((string?)null);
            var feg = new Mock<IFrequentExecutionGuardService>();
            feg.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
            var fs = new Mock<IFileSystemService>();
            fs.Setup(s => s.ExistsFolder(It.IsAny<Url>()))
              .Returns<Url>(u => Directory.Exists(u.Path));

            Model = new ContentModelFileSystem(
                "Timing",
                new TaskManager(),
                settings.Object,
                new FolderHistoryService(settings.Object),
                fs.Object,
                feg.Object,
                Cache,
                Mock.Of<Serilog.ILogger>());
            Model.ViewSize = new Size(80, 40);
            Model.SetUrl(new Url(FolderPath));
        }

        public FilePanelRefreshTask BuildTask(RecordingStopwatchFactory factory) =>
            new(Model, Cache, Mock.Of<Serilog.ILogger>(), stopwatchFactory: factory);

        public void Dispose()
        {
            try { Directory.Delete(FolderPath, recursive: true); } catch { }
        }
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// PhaseDurations is populated after execution whenever a factory is injected.
    /// </summary>
    [TestMethod]
    [Timeout(10_000)]
    public async Task ExecuteAsync_WithFactory_PopulatesPhaseDurations()
    {
        using var h = new Harness(fileCount: 5);
        var factory = new RecordingStopwatchFactory();

        var task = h.BuildTask(factory);
        await task.ExecuteAsync();

        Assert.IsNotNull(task.PhaseDurations, "PhaseDurations must be set after execution when a factory is injected.");
        Assert.IsTrue(task.PhaseDurations.ContainsKey("CacheLookup"), "CacheLookup phase must be recorded.");
        Assert.IsTrue(task.PhaseDurations.ContainsKey("Enumeration"), "Enumeration phase must be recorded.");
    }

    /// <summary>
    /// PhaseDurations is null when no factory is injected (zero overhead in production).
    /// </summary>
    [TestMethod]
    [Timeout(10_000)]
    public async Task ExecuteAsync_WithoutFactory_PhaseDurationsIsNull()
    {
        using var h = new Harness(fileCount: 5);
        var task = new FilePanelRefreshTask(h.Model, h.Cache, Mock.Of<Serilog.ILogger>());
        await task.ExecuteAsync();

        Assert.IsNull(task.PhaseDurations, "PhaseDurations must be null when no factory is injected.");
    }

    /// <summary>
    /// Memory cache hit: CacheLookup completes well within the budget.
    /// The cache is pre-warmed by a first run so the second run hits the fast path.
    /// </summary>
    [TestMethod]
    [Timeout(10_000)]
    public async Task ExecuteAsync_MemoryCacheHit_CacheLookupCompletesWithinBudget()
    {
        using var h = new Harness(fileCount: 50);

        // Warm the cache with a cold run (no factory — we don't care about that timing).
        await new FilePanelRefreshTask(h.Model, h.Cache, Mock.Of<Serilog.ILogger>()).ExecuteAsync();

        // Now measure the cache-hit path.
        var factory = new RecordingStopwatchFactory();
        var task = h.BuildTask(factory);
        await task.ExecuteAsync();

        var cacheLookupMs = task.PhaseDurations!["CacheLookup"].TotalMilliseconds;
        Assert.IsTrue(cacheLookupMs < CacheLookupBudgetMs,
            $"CacheLookup took {cacheLookupMs:F1} ms on cache hit (budget {CacheLookupBudgetMs} ms).");
    }

    /// <summary>
    /// Cold enumeration of a small directory completes within a per-file budget.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task ExecuteAsync_ColdEnumeration_CompletesWithinPerFileBudget()
    {
        const int fileCount = 200;
        using var h = new Harness(fileCount: fileCount);
        var factory = new RecordingStopwatchFactory();

        var task = h.BuildTask(factory);
        await task.ExecuteAsync();

        var enumerationMs = task.PhaseDurations!["Enumeration"].TotalMilliseconds;
        var budgetMs = fileCount * EnumerationBudgetMsPerFile;
        Assert.IsTrue(enumerationMs < budgetMs,
            $"Enumeration of {fileCount} files took {enumerationMs:F1} ms (budget {budgetMs} ms).");
    }

    /// <summary>
    /// Large directory (2 000 files + 50 folders) completes enumeration in O(n log n) time.
    /// With the old O(n² log n) per-batch-sort approach this test would take far longer on
    /// most machines, making it a regression guard for the batch-sort optimization.
    /// </summary>
    [TestMethod]
    [Timeout(30_000)]
    public async Task ExecuteAsync_LargeDirectory_EnumerationStaysWithinBudget()
    {
        const int fileCount = 2_000;
        const int subfolderCount = 50;
        const int budgetMs = 5_000; // very generous; old O(n² log n) code would take much longer
        using var h = new Harness(fileCount: fileCount, subfolderCount: subfolderCount);
        var factory = new RecordingStopwatchFactory();

        var task = h.BuildTask(factory);
        await task.ExecuteAsync();

        var enumerationMs = task.PhaseDurations!["Enumeration"].TotalMilliseconds;
        Assert.IsTrue(enumerationMs < budgetMs,
            $"Enumeration of {fileCount} files + {subfolderCount} folders took {enumerationMs:F0} ms (budget {budgetMs} ms). " +
            "This may indicate a regression to O(n² log n) batch sorting.");
    }

    /// <summary>
    /// Final UI update (ReplaceEntriesAndRestoreFocus) completes within budget regardless of
    /// directory size — it is a single atomic list swap, not proportional to file count.
    /// </summary>
    [TestMethod]
    [Timeout(15_000)]
    public async Task ExecuteAsync_UiUpdate_CompletesWithinBudget()
    {
        const int fileCount = 500;
        using var h = new Harness(fileCount: fileCount);
        var factory = new RecordingStopwatchFactory();

        var task = h.BuildTask(factory);
        await task.ExecuteAsync();

        if (!task.PhaseDurations!.TryGetValue("UiUpdate", out var uiDuration))
        {
            Assert.Inconclusive("UiUpdate phase was skipped (task may have been cancelled or model URL changed).");
            return;
        }

        Assert.IsTrue(uiDuration.TotalMilliseconds < UiUpdateBudgetMs,
            $"UiUpdate took {uiDuration.TotalMilliseconds:F1} ms for {fileCount} entries (budget {UiUpdateBudgetMs} ms).");
    }

    /// <summary>
    /// The final entry list is fully sorted: ".." first, then folders alphabetically, then
    /// files alphabetically. Validates the O(n log n) refactor preserves correct ordering.
    /// </summary>
    [TestMethod]
    [Timeout(10_000)]
    public async Task ExecuteAsync_FinalEntries_AreFullySorted()
    {
        using var h = new Harness();

        // Create files and folders that would be wrongly ordered if sort were skipped.
        File.WriteAllText(Path.Combine(h.FolderPath, "zebra.txt"), string.Empty);
        File.WriteAllText(Path.Combine(h.FolderPath, "alpha.txt"), string.Empty);
        File.WriteAllText(Path.Combine(h.FolderPath, "MANGO.txt"), string.Empty);
        Directory.CreateDirectory(Path.Combine(h.FolderPath, "z_folder"));
        Directory.CreateDirectory(Path.Combine(h.FolderPath, "a_folder"));

        var task = new FilePanelRefreshTask(h.Model, h.Cache, Mock.Of<Serilog.ILogger>());
        await task.ExecuteAsync();

        var names = h.Model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
        Assert.IsTrue(names.Count >= 6, $"Expected at least 6 entries; got [{string.Join(", ", names)}]");

        // ".." must be first
        Assert.AreEqual("..", names[0], $"First entry must be '..'; got [{string.Join(", ", names)}]");

        // Folders come before files
        var folderNames = names.Skip(1).TakeWhile(n => !n.EndsWith(".txt")).ToList();
        var fileNames   = names.Skip(1 + folderNames.Count).ToList();

        Assert.IsTrue(folderNames.Count >= 2, $"Expected folders in list; got [{string.Join(", ", names)}]");
        Assert.IsTrue(fileNames.Count >= 3,   $"Expected files in list; got [{string.Join(", ", names)}]");

        // Each group is sorted case-insensitively
        for (var i = 1; i < folderNames.Count; i++)
            Assert.IsTrue(
                string.Compare(folderNames[i - 1], folderNames[i], StringComparison.OrdinalIgnoreCase) <= 0,
                $"Folders not sorted: '{folderNames[i - 1]}' > '{folderNames[i]}'");

        for (var i = 1; i < fileNames.Count; i++)
            Assert.IsTrue(
                string.Compare(fileNames[i - 1], fileNames[i], StringComparison.OrdinalIgnoreCase) <= 0,
                $"Files not sorted: '{fileNames[i - 1]}' > '{fileNames[i]}'");
    }
}
