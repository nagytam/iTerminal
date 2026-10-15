using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>
/// A panel comes back where it was left. The whole difficulty is timing: a file panel enumerates its
/// folder on a background task, so the position cannot simply be assigned while the app is starting —
/// at that moment the listing is one <c>..</c> row and anything set against it is clamped to zero.
/// These tests run the real panel, the real task manager and a settings service that outlives the
/// "restart", because that clamp is invisible to anything that fakes the load.
/// </summary>
[TestClass]
public class FilePanelPositionRestoreTest
{
    // A deadline, not a budget: the listing loads on the TaskManager, which the rest of the suite
    // shares when the whole solution runs. The load-time budget is asserted elsewhere, on PhaseDurations.
    private const int LoadTimeoutMs = 20_000;

    private string _tempRoot = string.Empty;
    private string _folder = string.Empty;
    private SettingsService _settingsService = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FilePanelRestoreTest_" + Guid.NewGuid().ToString("N")[..8]);
        _folder = Path.Combine(_tempRoot, "folder");
        Directory.CreateDirectory(_folder);

        // Enough rows that a cursor half way down is a position and not an accident.
        for (var index = 0; index < 30; index++)
        {
            File.WriteAllText(Path.Combine(_folder, $"file_{index:00}.txt"), "x");
        }

        _settingsService = new SettingsService();
        _settingsService.Set(ContentModelService.Left, "file://" + _folder);
        _settingsService.Set(ContentModelService.Right, "file://" + _folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    /// <summary>One run of the app: its own service stack, over the settings that outlive it.</summary>
    private FilePanel StartPanel(string name)
    {
        var taskManager = new TaskManager();
        var memoryCacheService = new MemoryCacheService();
        var themeService = new ThemeService();
        var logger = Serilog.Core.Logger.None;

        var contentModelService = new ContentModelService(
            taskManager,
            _settingsService,
            new FileSystemService(),
            Mock.Of<IMemoryDriveService>(),
            Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(),
            Mock.Of<IIPhoneFileSystemService>(),
            Mock.Of<INetworkProfileService>(),
            Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            new FolderHistoryService(_settingsService),
            new FrequentExecutionGuardService(new DateTimeProviderService()),
            memoryCacheService,
            logger);

        var factory = new FilePanelFactory(
            taskManager,
            contentModelService,
            new TerminalModel(taskManager, contentModelService),
            memoryCacheService,
            _settingsService,
            logger,
            themeService);

        FilePanel panel = factory.Create(name);
        panel.ViewSize = new Size(80, 24);
        return panel;
    }

    private static async Task<ContentModelFileSystem> LoadedModelAsync(FilePanel panel)
    {
        await panel.OnInitializeAsync();

        var model = (ContentModelFileSystem)panel.Model!;
        var deadline = DateTime.UtcNow.AddMilliseconds(LoadTimeoutMs);
        while (DateTime.UtcNow < deadline && model.FilteredEntries.Count <= 1)
        {
            await Task.Delay(20);
        }

        Assert.IsTrue(model.FilteredEntries.Count > 1, "Precondition: the folder must have loaded");
        return model;
    }

    /// <summary>
    /// Waits for the condition and then lets the panel settle before believing it. Several refresh
    /// tasks can be replacing the same listing while a panel starts up, so a position that is right
    /// for one poll and wrong for the next is not a restored position.
    /// </summary>
    private static async Task<bool> SettlesOnAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(LoadTimeoutMs);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(20);
        }

        await Task.Delay(300);
        return condition();
    }

    [TestMethod]
    public async Task Panel_RestoresTheCursorAndTheFirstVisibleRow()
    {
        FilePanel panel = StartPanel(ContentModelService.Left);
        ContentModelFileSystem model = await LoadedModelAsync(panel);

        // The listing streams in batches, so "more than the .. row" is not yet "row 20 exists".
        Assert.IsTrue(
            await SettlesOnAsync(() => model.FilteredEntries.Count > 20),
            $"Precondition: row 20 must have loaded (have {model.FilteredEntries.Count})");

        model.CursorPosition.Y = 20;
        model.ScrollPosition.Y = 10;
        var expectedEntry = model.FilteredEntries[20].Url.FullUrl;
        panel.Close();

        FilePanel restarted = StartPanel(ContentModelService.Left);
        ContentModelFileSystem restoredModel = await LoadedModelAsync(restarted);

        Assert.IsTrue(
            await SettlesOnAsync(() => restoredModel.CursorPosition.Y == 20),
            $"The cursor came back at {restoredModel.CursorPosition.Y} instead of 20");
        Assert.AreEqual(expectedEntry, restoredModel.GetFocusedEntry()?.Url.FullUrl);
        Assert.AreEqual(10u, restoredModel.ScrollPosition.Y, "The first visible row is part of the position");
    }

    /// <summary>
    /// The left panel used to be zeroed on purpose while only the right one tried to restore — and
    /// the right one restored an index against a folder that had not loaded yet, which the clamp in
    /// <c>Update</c> turned straight back into zero. Both panels are covered here for that reason.
    /// </summary>
    [TestMethod]
    public async Task BothPanels_RestoreTheirOwnPosition()
    {
        FilePanel left = StartPanel(ContentModelService.Left);
        FilePanel right = StartPanel(ContentModelService.Right);
        ContentModelFileSystem leftModel = await LoadedModelAsync(left);
        ContentModelFileSystem rightModel = await LoadedModelAsync(right);

        leftModel.CursorPosition.Y = 5;
        rightModel.CursorPosition.Y = 12;
        left.Close();
        right.Close();

        ContentModelFileSystem restoredLeft = await LoadedModelAsync(StartPanel(ContentModelService.Left));
        ContentModelFileSystem restoredRight = await LoadedModelAsync(StartPanel(ContentModelService.Right));

        Assert.IsTrue(
            await SettlesOnAsync(() => restoredLeft.CursorPosition.Y == 5),
            $"The left panel came back at {restoredLeft.CursorPosition.Y} instead of 5");
        Assert.IsTrue(
            await SettlesOnAsync(() => restoredRight.CursorPosition.Y == 12),
            $"The right panel came back at {restoredRight.CursorPosition.Y} instead of 12");
    }

    /// <summary>
    /// The row is restored by URL, not by index: a folder is rarely the same list it was, and an
    /// index alone would land the cursor on whatever has taken that row's place.
    /// </summary>
    [TestMethod]
    public async Task Panel_LandsOnTheSameRow_WhenTheFolderGainedEntriesAboveIt()
    {
        FilePanel panel = StartPanel(ContentModelService.Left);
        ContentModelFileSystem model = await LoadedModelAsync(panel);

        // The listing streams in batches, so "more than the .. row" is not yet "row 20 exists".
        Assert.IsTrue(
            await SettlesOnAsync(() => model.FilteredEntries.Count > 20),
            $"Precondition: row 20 must have loaded (have {model.FilteredEntries.Count})");

        model.CursorPosition.Y = 20;
        var expectedEntry = model.FilteredEntries[20].Url.FullUrl;
        panel.Close();

        // Three files that sort before it, so the row it was on is now three lower.
        foreach (var name in new[] { "aaa_1.txt", "aaa_2.txt", "aaa_3.txt" })
        {
            File.WriteAllText(Path.Combine(_folder, name), "x");
        }

        ContentModelFileSystem restoredModel = await LoadedModelAsync(StartPanel(ContentModelService.Left));

        // The first listing may come from the folder cache, without the three new files — the cursor
        // is already on the right entry then, at its old row. Only the fresh listing moves it to row
        // 23, and under load that can arrive well after the first, so it is waited for as well.
        Assert.IsTrue(
            await SettlesOnAsync(() => restoredModel.GetFocusedEntry()?.Url.FullUrl == expectedEntry
                && restoredModel.FilteredEntries.Any(e => e.Name == "aaa_3.txt")),
            $"Landed on {restoredModel.GetFocusedEntry()?.Url.DisplayName} rather than the remembered row");
        Assert.AreEqual(23u, restoredModel.CursorPosition.Y, "…which is now three rows further down");
    }

    /// <summary>
    /// A restore describes the folder the panel opened with. Once the user navigates, it is stale
    /// and must not be applied to whatever they navigated to.
    /// </summary>
    [TestMethod]
    public async Task NavigatingBeforeTheRestoreLands_DropsIt()
    {
        FilePanel panel = StartPanel(ContentModelService.Left);
        ContentModelFileSystem model = await LoadedModelAsync(panel);
        model.CursorPosition.Y = 20;
        panel.Close();

        FilePanel restarted = StartPanel(ContentModelService.Left);
        await restarted.OnInitializeAsync();

        var restoredModel = (ContentModelFileSystem)restarted.Model!;
        restoredModel.SetUrl(new Url("file://" + _tempRoot));

        await LoadedModelAsync(restarted);
        await Task.Delay(200);

        Assert.AreEqual(0u, restoredModel.CursorPosition.Y, "The stale position must not follow the panel");
    }
}
