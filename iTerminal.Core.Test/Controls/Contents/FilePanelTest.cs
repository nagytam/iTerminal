using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

// Several tests here drive a real TaskManager/FilePanelRefreshTask pipeline against the real
// filesystem with WaitForEntriesAsync polling and hard [Timeout] budgets. Run isolated from
// other classes (which run in parallel per the assembly-level Parallelize setting) so
// CPU/thread-pool contention from unrelated tests doesn't push these over budget — same
// approach as FilePanelRefreshTaskTimingTest and FilePanelIntegrationTest.
[TestClass]
[DoNotParallelize]
public class FilePanelTest
{
    private sealed class TestContentBrowser(IThemeService themeService) : ContentBrowser(
        name: "TestBrowser",
        contentModelService: Mock.Of<IContentModelService>(),
        terminalModel: new Mock<iTerminal.Core.Model.TerminalModel>(
            MockBehavior.Loose,
            Mock.Of<iConsole.Tasks.TaskManager>(),
            Mock.Of<IContentModelService>()).Object,
        taskManager: Mock.Of<iConsole.Tasks.TaskManager>(),
        themeService: themeService)
    {
        public new void SetFocus() => base.SetFocus();
        public new void ResetFocus() => base.ResetFocus();
    }

    private sealed class TestItemInformation(IThemeService themeService) : ItemInformation(
        contentModelService: Mock.Of<IContentModelService>(),
        taskManager: Mock.Of<iConsole.Tasks.TaskManager>(),
        memoryCacheService: new MemoryCacheService(),
        themeService: themeService,
        logger: Mock.Of<Serilog.ILogger>())
    {
    }

    private static FilePanel CreateSut(
        string name,
        Mock<ISettingsService> settingsService,
        Mock<IContentModelService> contentModelService,
        Mock<IContentModel> contentModel,
        IThemeService themeService)
    {
        var contentBrowser = new TestContentBrowser(themeService);
        var itemInformation = new TestItemInformation(themeService);

        contentModelService
            .Setup(s => s.AddContentModel(name, It.IsAny<Url>()))
            .Returns(contentModel.Object);

        return new FilePanel(
            name,
            contentBrowser,
            itemInformation,
            contentModelService.Object,
            settingsService.Object,
            themeService);
    }

    private static Mock<IContentModel> CreateContentModelMock()
    {
        var m = new Mock<IContentModel>(MockBehavior.Loose);

        // Provide non-null defaults; individual tests can override with their own SetupProperty values.
        m.SetupProperty(x => x.ScrollPosition, new Position(0, 0));
        m.SetupProperty(x => x.CursorPosition, new Position(0, 0));
        m.SetupProperty(x => x.ViewSize, new iConsole.Data.Size(0, 0));
        m.SetupProperty(x => x.ContentBrowser, null!);

        return m;
    }

    /// <summary>
    /// Sets up a strict settings mock holding a saved position for <paramref name="name"/>.
    /// </summary>
    private static Mock<ISettingsService> SettingsWithSavedPosition(
        string name, string? scrollY, string? cursorY, string? focusedEntry = null)
    {
        var settingsService = new Mock<ISettingsService>(MockBehavior.Strict);
        settingsService.Setup(s => s.Get(name)).Returns("file:///");
        settingsService.Setup(s => s.Get(name + "_ScrollPosition.Y")).Returns(scrollY);
        settingsService.Setup(s => s.Get(name + "_CursorPosition.Y")).Returns(cursorY);
        settingsService.Setup(s => s.Get(name + "_FocusedEntry")).Returns(focusedEntry);
        return settingsService;
    }

    /// <summary>
    /// The panel reads the saved position and hands it to the model rather than assigning it. It
    /// cannot assign it: the folder is enumerated on a background task, and a cursor set here — with
    /// the listing still one ".." row — is clamped straight back to zero.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task OnInitializeAsync_WhenRightPanelSettingsContainPositions_HandsThemToTheModelAndReadsUrl()
    {
        var name = "RightPanel";
        var settingsService = SettingsWithSavedPosition(name, "12", "34", "file:///beta.txt");

        var contentModelService = new Mock<IContentModelService>(MockBehavior.Strict);

        var contentModel = CreateContentModelMock();
        contentModel.Setup(m => m.ReadUrl());
        contentModel.Setup(m => m.RestoreNavigation(It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>()));

        var sut = CreateSut(name, settingsService, contentModelService, contentModel, new ThemeService());
        sut.ViewSize = new iConsole.Data.Size(80, 10);
        await sut.OnInitializeAsync();

        contentModel.Verify(m => m.RestoreNavigation("file:///beta.txt", 34, 12), Times.Once);
        contentModel.Verify(m => m.ReadUrl(), Times.Once);
    }

    /// <summary>
    /// The left panel used to be zeroed here on purpose while only the right one restored anything.
    /// Both sides now come back where they were left.
    /// </summary>
    [TestMethod]
    [Timeout(5000)]
    public async Task OnInitializeAsync_WhenLeftPanelSettingsContainPositions_RestoresThemToo()
    {
        var name = "LeftPanel";
        var settingsService = SettingsWithSavedPosition(name, "12", "34", "file:///beta.txt");

        var contentModelService = new Mock<IContentModelService>(MockBehavior.Strict);

        var contentModel = CreateContentModelMock();
        contentModel.Setup(m => m.ReadUrl());
        contentModel.Setup(m => m.RestoreNavigation(It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>()));

        var sut = CreateSut(name, settingsService, contentModelService, contentModel, new ThemeService());
        sut.ViewSize = new iConsole.Data.Size(80, 10);
        await sut.OnInitializeAsync();

        contentModel.Verify(m => m.RestoreNavigation("file:///beta.txt", 34, 12), Times.Once);
        contentModel.Verify(m => m.ReadUrl(), Times.Once);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task OnInitializeAsync_WhenSettingsContainInvalidValues_RestoresTheTopOfTheListing()
    {
        var name = "RightPanel";
        var settingsService = SettingsWithSavedPosition(name, "NaN", null);

        var contentModelService = new Mock<IContentModelService>(MockBehavior.Strict);

        var contentModel = CreateContentModelMock();
        // Seed defaults via SetupProperty so the proxy stores the value.
        contentModel.SetupProperty(m => m.ScrollPosition, new Position(0, 5));
        contentModel.SetupProperty(m => m.CursorPosition, new Position(0, 7));
        contentModel.Setup(m => m.ReadUrl());
        contentModel.Setup(m => m.RestoreNavigation(It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>()));

        var sut = CreateSut(name, settingsService, contentModelService, contentModel, new ThemeService());
        sut.ViewSize = new iConsole.Data.Size(80, 10);
        await sut.OnInitializeAsync();

        // Unreadable settings restore nothing rather than something arbitrary, and the panel never
        // writes the position itself — that is the model's to place once its listing exists.
        contentModel.Verify(m => m.RestoreNavigation(null, 0, 0), Times.Once);
        Assert.AreEqual(5, sut.Model!.ScrollPosition.Y);
        Assert.AreEqual(7, sut.Model.CursorPosition.Y);
        contentModel.Verify(m => m.ReadUrl(), Times.Once);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task OnBeforeClose_PersistsPositionsToSettings()
    {
        var name = "LeftPanel";
        var keyScrollY = name + "_ScrollPosition.Y";
        var keyCursorY = name + "_CursorPosition.Y";

        var settingsService = SettingsWithSavedPosition(name, null, null);
        settingsService.Setup(s => s.Set(keyScrollY, "9"));
        settingsService.Setup(s => s.Set(keyCursorY, "8"));
        settingsService.Setup(s => s.Set(name + "_FocusedEntry", "file:///gamma.txt"));

        var contentModelService = new Mock<IContentModelService>(MockBehavior.Strict);

        var contentModel = CreateContentModelMock();
        contentModel.Setup(m => m.ReadUrl());
        contentModel.Setup(m => m.RestoreNavigation(It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<long>()));
        contentModel
            .Setup(m => m.GetFocusedEntry())
            .Returns(new FileSystemEntry("gamma.txt", "file:///gamma.txt"));

        var sut = CreateSut(name, settingsService, contentModelService, contentModel, new ThemeService());
        sut.ViewSize = new iConsole.Data.Size(80, 10);
        await sut.OnInitializeAsync();

        // simulate user moving the scroll/cursor after initialization
        sut.Model!.ScrollPosition.Y = 9;
        sut.Model.CursorPosition.Y = 8;

        typeof(FilePanel)
            .GetMethod("OnBeforeClose", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(sut, null);

        settingsService.VerifyAll();
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task RefreshCommand_WhenDirectoryChanges_RefreshesEntriesImmediately()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        var existingFile = Path.Combine(folderPath, "existing.txt");
        var addedFile = Path.Combine(folderPath, "added.txt");
        File.WriteAllText(existingFile, "existing");
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
            settingsService.Setup(s => s.Get(SettingsService.FOLDER_HISTORY)).Returns((string?)null);
            var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
            frequentExecutionGuardService
                .Setup(g => g.Execute(It.IsAny<Action>()))
                .Callback<Action>(action => action());
            var fileSystemService = new Mock<IFileSystemService>();
            fileSystemService
                .Setup(s => s.ExistsFolder(It.IsAny<Url>()))
                .Returns<Url>(url => Directory.Exists(url.Path));

            var model = new ContentModelFileSystem(
                name: "TestPanel",
                taskManager: new TaskManager(),
                settingsService: settingsService.Object,
                folderHistoryService: new FolderHistoryService(settingsService.Object),
                fileSystemService: fileSystemService.Object,
                frequentExecutionGuardService: frequentExecutionGuardService.Object,
                memoryCacheService: memoryCacheService,
                logger: Mock.Of<Serilog.ILogger>());

            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(folderPath));

            await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == "existing.txt"));

            File.WriteAllText(addedFile, "added");
            var refreshTask = new FilePanelRefreshTask(model, memoryCacheService, Mock.Of<Serilog.ILogger>());
            await refreshTask.ExecuteAsync();

            var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
            CollectionAssert.Contains(names, "existing.txt");
            CollectionAssert.Contains(names, "added.txt");
        }
        finally
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task FilePanelRefreshTask_WhenEntriesShift_KeepsFocusedItemByUrl()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "alpha.txt"), "alpha");
        File.WriteAllText(Path.Combine(folderPath, "beta.txt"), "beta");
        File.WriteAllText(Path.Combine(folderPath, "gamma.txt"), "gamma");
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(folderPath));

            await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == "beta.txt"));

            var betaIndex = model.FilteredEntries
                .Select((entry, index) => new { entry, index })
                .Single(item => item.entry.Url.DisplayName == "beta.txt")
                .index;

            model.CursorPosition.Y = betaIndex;
            model.Update();

            File.WriteAllText(Path.Combine(folderPath, "aardvark.txt"), "aardvark");

            var refreshTask = new FilePanelRefreshTask(model, memoryCacheService, Mock.Of<Serilog.ILogger>());
            await refreshTask.ExecuteAsync();

            var refreshedBetaIndex = model.FilteredEntries
                .Select((entry, index) => new { entry, index })
                .Single(item => item.entry.Url.DisplayName == "beta.txt")
                .index;

            Assert.AreEqual("beta.txt", model.GetFocusedEntry()?.Url.DisplayName);
            Assert.AreEqual(refreshedBetaIndex, (int)model.CursorPosition.Y);
        }
        finally
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }
    }

    private static async Task WaitForEntriesAsync(ContentModelFileSystem model, Func<IList<iConsole.Interfaces.IEntry>, bool> predicate)
    {
        for (var i = 0; i < 50; i++)
        {
            var entries = model.Entries.AsSnapshot();
            if (predicate(entries))
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Timed out waiting for file panel entries to refresh.");
    }

    private static ContentModelFileSystem CreateFileSystemModel(string folderPath, IMemoryCacheService memoryCacheService, TaskManager? taskManager = null)
    {
        var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
        settingsService.Setup(s => s.Get(SettingsService.FOLDER_HISTORY)).Returns((string?)null);
        var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
        frequentExecutionGuardService
            .Setup(g => g.Execute(It.IsAny<Action>()))
            .Callback<Action>(action => action());
        var fileSystemService = new Mock<IFileSystemService>();
        fileSystemService
            .Setup(s => s.ExistsFolder(It.IsAny<Url>()))
            .Returns<Url>(url => Directory.Exists(url.Path));

        return new ContentModelFileSystem(
            name: "TestPanel",
            taskManager: taskManager ?? new TaskManager(),
            settingsService: settingsService.Object,
            folderHistoryService: new FolderHistoryService(settingsService.Object),
            fileSystemService: fileSystemService.Object,
            frequentExecutionGuardService: frequentExecutionGuardService.Object,
            memoryCacheService: memoryCacheService,
            logger: Mock.Of<Serilog.ILogger>());
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task CtrlPageUp_WhenFocused_NavigatesToParentFolder()
    {
        // Arrange — real folder hierarchy in temp
        var parentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var childPath  = Path.Combine(parentPath, "child");
        Directory.CreateDirectory(childPath);

        try
        {
            var settingsService = new Mock<ISettingsService>(MockBehavior.Loose);
            settingsService.Setup(s => s.Get(SettingsService.FOLDER_HISTORY)).Returns((string?)null);
            settingsService.Setup(s => s.Get("LeftPanel")).Returns(childPath);

            var frequentExecutionGuardService = new Mock<IFrequentExecutionGuardService>();
            frequentExecutionGuardService
                .Setup(g => g.Execute(It.IsAny<Action>()))
                .Callback<Action>(action => action());

            var fileSystemService = new Mock<IFileSystemService>();
            fileSystemService
                .Setup(s => s.ExistsFolder(It.IsAny<Url>()))
                .Returns<Url>(url => Directory.Exists(url.Path));

            var taskManager        = new TaskManager();
            var memoryCacheService = new MemoryCacheService();
            var folderHistoryService = new FolderHistoryService(settingsService.Object);

            var contentModel = new ContentModelFileSystem(
                name:                          "LeftPanel",
                taskManager:                   taskManager,
                settingsService:               settingsService.Object,
                folderHistoryService:          folderHistoryService,
                fileSystemService:             fileSystemService.Object,
                frequentExecutionGuardService: frequentExecutionGuardService.Object,
                memoryCacheService:            memoryCacheService,
                logger:                        Mock.Of<Serilog.ILogger>());

            contentModel.ViewSize = new iConsole.Data.Size(80, 20);
            contentModel.SetUrl(new Url(childPath));

            Url? navigatedUrl = null;
            var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
            contentModelService
                .Setup(s => s.AddContentModel("LeftPanel", It.IsAny<Url>()))
                .Returns(contentModel);
            contentModelService
                .Setup(s => s.SetUrlAsync("LeftPanel", It.IsAny<Url>()))
                .Callback<string, Url>((_, url) => navigatedUrl = url)
                .Returns(Task.CompletedTask);

            var terminalModel = new Mock<iTerminal.Core.Model.TerminalModel>(
                MockBehavior.Loose,
                Mock.Of<iConsole.Tasks.TaskManager>(),
                Mock.Of<IContentModelService>()).Object;

            var themeService = new ThemeService();
            var contentBrowser = new ContentBrowser(
                name:                "LeftPanel",
                contentModelService: contentModelService.Object,
                terminalModel:       terminalModel,
                taskManager:         taskManager,
                themeService:        themeService);

            var itemInformation = new TestItemInformation(themeService);

            var sut = new FilePanel(
                name:                  "LeftPanel",
                contentBrowser:        contentBrowser,
                itemInformation:       itemInformation,
                contentModelService:   contentModelService.Object,
                settingsService:       settingsService.Object,
                themeService:          themeService);

            sut.ViewSize = new iConsole.Data.Size(80, 24);
            await sut.OnInitializeAsync();
            sut.SetFocus();

            // Act — Ctrl+PageUp
            var keyEvent = new KeyEvent('\0', ConsoleKey.PageUp, shift: false, alt: false, control: true);
            var handled  = await sut.HandleKeyEventAsync(keyEvent);

            // Assert
            Assert.IsTrue(handled, "FilePanel should report Ctrl+PageUp as handled.");
            Assert.IsNotNull(navigatedUrl, "Ctrl+PageUp should trigger SetUrlAsync with the parent folder URL.");
            Assert.IsFalse(
                navigatedUrl!.FullUrl.Contains("child"),
                $"Navigated URL must point to the parent folder, not 'child'; got '{navigatedUrl.FullUrl}'.");
            Assert.IsTrue(
                navigatedUrl.FullUrl.Contains(Path.GetFileName(parentPath)),
                $"Navigated URL should contain '{Path.GetFileName(parentPath)}'; got '{navigatedUrl.FullUrl}'.");
        }
        finally
        {
            if (Directory.Exists(parentPath))
                Directory.Delete(parentPath, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetHeader_WhenViewSizeIsZero_StillReturnsPath()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            // ViewSize is intentionally left at (0,0) to simulate pre-layout state
            model.SetUrl(new Url(folderPath));

            var header = model.GetHeader(40);

            Assert.IsFalse(string.IsNullOrEmpty(header), "Header should not be empty even before layout sets ViewSize.");
        }
        finally
        {
            if (Directory.Exists(folderPath)) Directory.Delete(folderPath, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetHeader_WhenPathExceedsWidth_ShowsDrivePrefixAndFolderName()
    {
        // Arrange: use a path long enough to force truncation at a small display width.
        var folderName = Guid.NewGuid().ToString("N");   // 32-char name, guaranteed long
        var folderPath = Path.Combine(Path.GetTempPath(), folderName);
        Directory.CreateDirectory(folderPath);
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            model.SetUrl(new Url(folderPath));

            // Use a width narrow enough to force shrinking but wide enough to hold
            // the drive prefix + "..." + a few chars of the folder name.
            var header = model.GetHeader(20);

            // Drive prefix (e.g. "C:\") must appear at the start.
            var drive = Path.GetPathRoot(folderPath)!;   // e.g. "C:\"
            Assert.IsTrue(header.StartsWith(drive),
                $"Header '{header}' should start with drive prefix '{drive}'.");

            // The ellipsis separator must be present.
            Assert.IsTrue(header.Contains("..."),
                $"Header '{header}' should contain '...' as truncation marker.");

            // Result must never exceed the requested width.
            Assert.IsTrue(header.Length <= 20,
                $"Header '{header}' (len={header.Length}) must not exceed requested width 20.");
        }
        finally
        {
            if (Directory.Exists(folderPath)) Directory.Delete(folderPath, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetHeader_AfterResizeToLarger_NeverExceedsWidth()
    {
        // Use a deep path to guarantee truncation at narrow widths.
        var level1 = Path.Combine(Path.GetTempPath(), "hdr_level1");
        var folderPath = Path.Combine(level1, "level2", "level3", "level4");
        Directory.CreateDirectory(folderPath);
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            model.SetUrl(new Url(folderPath));

            var drive = Path.GetPathRoot(folderPath)!;
            var sep = Path.DirectorySeparatorChar;

            // Simulate resize by calling GetHeader with increasing widths.
            for (var w = 5; w <= folderPath.Length + 5; w++)
            {
                var header = model.GetHeader(w);

                // Must never be longer than the requested width.
                Assert.IsTrue(header.Length <= w,
                    $"width={w}: header '{header}' (len={header.Length}) exceeds width.");

                // When using drive-anchored truncation the tail must start on a
                // separator (clean folder boundary) wherever possible.
                if (header.Contains("...") && header.StartsWith(drive))
                {
                    var ellipsisPos = header.IndexOf("...");
                    var tail = header[(ellipsisPos + 3)..];
                    // The tail is allowed to start mid-name only when the final
                    // folder component itself is longer than the available tail budget.
                    if (tail.Length > 0 && tail[0] != sep)
                    {
                        // Verify that no separator fits: if we could have snapped
                        // forward or backward to a sep, the result would already have done so.
                        var tailStartInPath = folderPath.IndexOf(tail, StringComparison.Ordinal);
                        if (tailStartInPath > 0)
                        {
                            var prevSep = folderPath.LastIndexOf(sep, tailStartInPath - 1);
                            var nextSep = folderPath.IndexOf(sep, tailStartInPath);
                            var availTailBudget = w - drive.Length - 3;
                            // A clean snap would be longer than the budget — mid-cut is the
                            // only option in this edge case.
                            var cleanSnapLength = nextSep >= 0
                                ? folderPath.Length - nextSep
                                : folderPath.Length - (prevSep >= 0 ? prevSep : 0);
                            Assert.IsTrue(cleanSnapLength > availTailBudget,
                                $"width={w}: header '{header}' tail '{tail}' should start on a separator when one fits.");
                        }
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(level1))
                Directory.Delete(level1, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public void SetUrl_WhenFolderIsNotRoot_AddsParentEntryImmediately()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);

            model.SetUrl(new Url(folderPath));

            var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
            CollectionAssert.Contains(names, "..");
            Assert.AreEqual("..", names[0]);
        }
        finally
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task LoadEntries_WhenFolderIsNotRoot_ContainsParentEntry()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(folderPath));

            await WaitForEntriesAsync(model, entries => entries.Count > 0 || !Directory.EnumerateFileSystemEntries(folderPath).Any());

            var refreshTask = new FilePanelRefreshTask(model, memoryCacheService, Mock.Of<Serilog.ILogger>());
            await refreshTask.ExecuteAsync();

            var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
            CollectionAssert.Contains(names, "..");
        }
        finally
        {
            if (Directory.Exists(folderPath)) Directory.Delete(folderPath, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task LoadEntries_WhenFolderIsRoot_DoesNotContainParentEntry()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var memoryCacheService = new MemoryCacheService();

        var model = CreateFileSystemModel(root, memoryCacheService);
        model.ViewSize = new iConsole.Data.Size(80, 20);
        model.SetUrl(new Url(root));

        await WaitForEntriesAsync(model, entries => entries.Count > 0);

        var refreshTask = new FilePanelRefreshTask(model, memoryCacheService, Mock.Of<Serilog.ILogger>());
        await refreshTask.ExecuteAsync();

        var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
        CollectionAssert.DoesNotContain(names, "..");
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task LoadEntries_WhenFolderIsNotRoot_ParentEntryIsFirst()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "aaa.txt"), "content");
        Directory.CreateDirectory(Path.Combine(folderPath, "aaa_folder"));
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(folderPath));

            await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == "aaa.txt"));

            var refreshTask = new FilePanelRefreshTask(model, memoryCacheService, Mock.Of<Serilog.ILogger>());
            await refreshTask.ExecuteAsync();

            var entries = model.Entries.AsSnapshot();
                    Assert.AreEqual("..", entries[0].Url.DisplayName);
                            }
                            finally
                            {
                                if (Directory.Exists(folderPath)) Directory.Delete(folderPath, recursive: true);
                            }
                        }

                            [TestMethod]
                            [Timeout(5000)]
                            public async Task Refresh_WhenPreviousTaskIsCancelled_StartsNewTask()
                            {
                                var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                                Directory.CreateDirectory(folderPath);
                                File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");
                                var memoryCacheService = new MemoryCacheService();
                                var taskManager = new TaskManager();

                                try
                                {
                                    var model = CreateFileSystemModel(folderPath, memoryCacheService, taskManager);
                                    model.ViewSize = new iConsole.Data.Size(80, 20);

                                    // First SetUrl queues a task — immediately cancel it to simulate the startup
                                    // double-SetUrl sequence (ContentPanel.OnInitializeAsync then FilePanel.ReadUrl).
                                    model.SetUrl(new Url(folderPath));
                                    foreach (var task in taskManager.Tasks.AsSnapshot().OfType<FilePanelRefreshTask>().ToList())
                                    {
                                        task.CancellationTokenSource.Cancel();
                                    }

                                    // Second SetUrl with the same URL must not be blocked by the cancelled task.
                                    model.SetUrl(new Url(folderPath));

                                    await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == "file.txt"));

                                    var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
                                    CollectionAssert.Contains(names, "file.txt");
                                    CollectionAssert.Contains(names, "..");
                                }
                                finally
                                {
                                    if (Directory.Exists(folderPath)) Directory.Delete(folderPath, recursive: true);
                                }
                            }

    // -------------------------------------------------------------------------
    // Prefetch + key handling tests
    // -------------------------------------------------------------------------

    [TestMethod]
    [Timeout(5000)]
    public async Task Enter_OnSubfolder_NavigatesToChildFolder()
    {
        var parentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var childPath  = Path.Combine(parentPath, "child");
        Directory.CreateDirectory(childPath);
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(parentPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(parentPath));

            await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == "child"));

            var childIndex = model.FilteredEntries
                .Select((entry, i) => (entry, i))
                .Single(x => x.entry.Url.DisplayName == "child")
                .i;
            model.CursorPosition.Y = (uint)childIndex;
            model.Update();

            Url? navigatedUrl = null;
            var contentModelService = new Mock<IContentModelService>(MockBehavior.Loose);
            contentModelService
                .Setup(s => s.SetUrlAsync("TestPanel", It.IsAny<Url>()))
                .Callback<string, Url>((_, url) => navigatedUrl = url)
                .Returns(Task.CompletedTask);

            var command = new NavigateToFolderCommand(model, contentModelService.Object);
            var keyEvent = new KeyEvent('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);
            var handled  = await command.HandleKeyEventAsync(keyEvent);

            Assert.IsTrue(handled, "NavigateToFolderCommand should handle the Enter key.");
            Assert.IsNotNull(navigatedUrl, "Enter on a subfolder should invoke SetUrlAsync.");
            Assert.IsTrue(
                navigatedUrl!.FullUrl.Contains("child"),
                $"Navigated URL should target 'child'; got '{navigatedUrl.FullUrl}'.");
        }
        finally
        {
            if (Directory.Exists(parentPath)) Directory.Delete(parentPath, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task Prefetch_AfterNavigation_WarmsCacheForParentFolder()
    {
        var rootPath  = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var childPath = Path.Combine(rootPath, "child");
        Directory.CreateDirectory(childPath);
        File.WriteAllText(Path.Combine(rootPath, "sibling.txt"), "s");
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(childPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(childPath));

            await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == ".."));

            // The prefetch key mirrors what FilePanelRefreshTask builds: "FilePanelRefreshTask" + url.FullUrl.
            var parentUrl = new Url(childPath).ParentUrl;
            var cacheKey  = "FilePanelRefreshTask" + parentUrl.FullUrl;

            await WaitForCacheAsync(memoryCacheService, cacheKey);

            var cached = memoryCacheService.Get(cacheKey) as List<IEntry>;
            Assert.IsNotNull(cached, "Parent folder should be in memory cache after prefetch.");
            Assert.IsTrue(cached!.Any(e => e.Url.DisplayName == "sibling.txt"),
                "Cached parent listing should contain 'sibling.txt'.");
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task Prefetch_AfterNavigation_WarmsCacheForSubfolders()
    {
        var rootPath   = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var subPath    = Path.Combine(rootPath, "sub");
        Directory.CreateDirectory(subPath);
        File.WriteAllText(Path.Combine(subPath, "content.txt"), "x");
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(rootPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);
            model.SetUrl(new Url(rootPath));

            await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == "sub"));

            // SchedulePrefetch uses new Url(DirectoryInfo) which appends DirectorySeparatorChar.
            var subDirUrl = new Url(new DirectoryInfo(subPath));
            var cacheKey  = "FilePanelRefreshTask" + subDirUrl.FullUrl;

            await WaitForCacheAsync(memoryCacheService, cacheKey);

            var cached = memoryCacheService.Get(cacheKey) as List<IEntry>;
            Assert.IsNotNull(cached, "Subfolder should be in memory cache after prefetch.");
            Assert.IsTrue(cached!.Any(e => e.Url.DisplayName == "content.txt"),
                "Cached subfolder listing should contain 'content.txt'.");
        }
        finally
        {
            if (Directory.Exists(rootPath)) Directory.Delete(rootPath, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task RapidNavigation_AlwaysDeliversFinalFolderEntries()
    {
        var root    = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var folderA = Path.Combine(root, "A");
        var folderB = Path.Combine(root, "B");
        var folderC = Path.Combine(root, "C");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);
        Directory.CreateDirectory(folderC);
        File.WriteAllText(Path.Combine(folderA, "file_a.txt"), "a");
        File.WriteAllText(Path.Combine(folderB, "file_b.txt"), "b");
        File.WriteAllText(Path.Combine(folderC, "file_c.txt"), "c");
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(root, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);

            // Navigate rapidly without waiting: A → B → C.
            // Intermediate tasks are cancelled; only C's task should update the model.
            model.SetUrl(new Url(folderA));
            model.SetUrl(new Url(folderB));
            model.SetUrl(new Url(folderC));

            await WaitForEntriesAsync(model, entries => entries.Any(e => e.Url.DisplayName == "file_c.txt"));

            var names = model.Entries.AsSnapshot().Select(e => e.Url.DisplayName).ToList();
            CollectionAssert.Contains(names, "file_c.txt");
            // Stale entries from cancelled intermediate navigations must not appear.
            CollectionAssert.DoesNotContain(names, "file_a.txt");
            CollectionAssert.DoesNotContain(names, "file_b.txt");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public void SetUrl_IsNonBlocking_BackgroundTaskEnqueuedNotInlined()
    {
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);
        for (var i = 0; i < 50; i++)
            File.WriteAllText(Path.Combine(folderPath, $"f{i:D3}.txt"), string.Empty);
        var memoryCacheService = new MemoryCacheService();

        try
        {
            var model = CreateFileSystemModel(folderPath, memoryCacheService);
            model.ViewSize = new iConsole.Data.Size(80, 20);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            model.SetUrl(new Url(folderPath));
            sw.Stop();

            Assert.IsTrue(sw.Elapsed < TimeSpan.FromMilliseconds(500),
                $"SetUrl took {sw.Elapsed.TotalMilliseconds:F1}ms — directory listing should run " +
                "in a background task, not block the caller.");
        }
        finally
        {
            if (Directory.Exists(folderPath)) Directory.Delete(folderPath, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task WaitForCacheAsync(MemoryCacheService memoryCacheService, string key, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (memoryCacheService.Get(key) is not null) return;
            await Task.Delay(50);
        }
        Assert.Fail($"Cache key '{key}' was not populated within {timeoutMs}ms.");
    }
                        }