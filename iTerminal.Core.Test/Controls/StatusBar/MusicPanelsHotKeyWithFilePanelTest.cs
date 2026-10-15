using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Services.Music;
using Moq;

namespace iTerminal.Core.Test.Controls.StatusBar;

/// <summary>
/// Routes the music chords through the real screen tree — a focused <see cref="FilePanel"/> next to
/// the real status bar — because the focused panel gets first refusal on every key.
///
/// <para>This is the harness that matters: an earlier version of this test used a plain control as
/// the focused sibling and happily passed for Alt+M, while in the running app the panel's
/// type-to-search filter was eating that chord before the status bar ever saw it.</para>
/// </summary>
[TestClass]
public class MusicPanelsHotKeyWithFilePanelTest
{
    private sealed class Harness : IAsyncDisposable
    {
        public required Screen Screen;
        public required FilePanel LeftPanel;
        public required FilePanel RightPanel;
        public required iTerminal.Core.Services.ContentModelService ContentModelService;
        public required MusicPlayerService Player;
        public required FakeMusicLibraryService Library;
        public required string TempRoot;

        public ValueTask DisposeAsync()
        {
            Player.Dispose();
            try { Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<Harness> BuildAsync()
    {
        var themeService = new ThemeService();

        var tempRoot = Path.Combine(Path.GetTempPath(), "MusicHotKeyPanelTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);

        var taskManager = new TaskManager();
        var memoryCacheService = new MemoryCacheService();
        var settingsService = new SettingsService();
        var folderHistory = new FolderHistoryService(settingsService);
        var frequentGuard = new FrequentExecutionGuardService(new DateTimeProviderService());
        var fileSystemService = new FileSystemService();
        var logger = Serilog.Core.Logger.None;

        settingsService.Set(iTerminal.Core.Services.ContentModelService.Left, "file://" + tempRoot);
        settingsService.Set(iTerminal.Core.Services.ContentModelService.Right, "file://" + tempRoot);

        var deviceService = new FakeAudioDeviceService();
        var player = new MusicPlayerService(deviceService);
        var library = new FakeMusicLibraryService()
            .Add("The Beatles", "Abbey Road", "Come Together")
            .Add("The Beatles", "Abbey Road", "Something")
            .Add("Miles Davis", "Kind of Blue", "So What");

        var contentModelService = new iTerminal.Core.Services.ContentModelService(
            taskManager, settingsService, fileSystemService,
            Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(), Mock.Of<IIPhoneFileSystemService>(),
            Mock.Of<INetworkProfileService>(), Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            folderHistory, frequentGuard, memoryCacheService, logger,
            dbContextFactory: null,
            stopwatchFactory: null,
            musicPlayerService: player,
            musicLibraryService: library,
            audioDeviceService: deviceService);

        var terminalModel = new TerminalModel(taskManager, contentModelService);
        var factory = new FilePanelFactory(
            taskManager, contentModelService, terminalModel, memoryCacheService, settingsService, logger, themeService);

        var leftPanel = factory.Create(iTerminal.Core.Services.ContentModelService.Left);
        leftPanel.ViewSize = new Size(80, 24);
        await leftPanel.OnInitializeAsync();

        var rightPanel = factory.Create(iTerminal.Core.Services.ContentModelService.Right);
        rightPanel.ViewSize = new Size(80, 24);
        await rightPanel.OnInitializeAsync();

        var hotKeys = new iTerminal.Core.Controls.StatusBar.HotKeys(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            contentModelService, folderHistory, null!, null!, null!, null!, settingsService, null!, themeService);
        var statusBar = new iTerminal.Core.Controls.StatusBar.TerminalStatusBar(
            new iConsole.Controls.PopUpWindows.DatePopUpWindow(themeService), hotKeys, themeService);

        var screen = new Screen(iConsole.Services.NullConsoleDriver.Instance, themeService);
        screen.AddControl(leftPanel);
        screen.AddControl(rightPanel);
        screen.AddControl(statusBar);
        await screen.OnInitializeAsync();

        leftPanel.SetFocus();
        Assert.IsTrue(leftPanel.IsFocused, "Precondition: the file panel must hold focus, as it does in the app");

        return new Harness
        {
            Screen = screen,
            LeftPanel = leftPanel,
            RightPanel = rightPanel,
            ContentModelService = contentModelService,
            Player = player,
            Library = library,
            TempRoot = tempRoot,
        };
    }

    [TestMethod]
    public async Task ControlF7_WithFilePanelFocused_ShowsTheTwoMusicPanels()
    {
        await using var h = await BuildAsync();

        var isHandled = await h.Screen.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.F7, shift: false, alt: false, control: true));

        Assert.IsTrue(isHandled, "Ctrl+F7 should be handled by the screen's control tree");
        Assert.IsInstanceOfType<ContentModelMusicCatalog>(h.ContentModelService.GetModel("LeftPanel"));
        Assert.IsInstanceOfType<ContentModelMusicPlaylist>(h.ContentModelService.GetModel("RightPanel"));
        Assert.IsNull(h.ContentModelService.GetModel("LeftPanel")!.Filter,
            "A function key must not start the panel's search filter");
    }

    [TestMethod]
    public async Task ControlShiftM_WithFilePanelFocused_ShowsTheTwoMusicPanels()
    {
        await using var h = await BuildAsync();

        var isHandled = await h.Screen.HandleKeyEventAsync(
            new KeyEvent('\r', ConsoleKey.M, shift: true, alt: false, control: true));

        Assert.IsTrue(isHandled, "Ctrl+Shift+M should be handled by the screen's control tree");
        Assert.IsInstanceOfType<ContentModelMusicCatalog>(h.ContentModelService.GetModel("LeftPanel"));
    }

    /// <summary>
    /// The marking flow through the real key router: Insert has to reach the panel's command rather
    /// than being eaten by type-to-search, and F5 has to add what was marked instead of what the
    /// cursor ended up on.
    /// </summary>
    [TestMethod]
    public async Task Insert_MarksCatalogRows_AndF5CopiesThemIntoThePlaylistPanel()
    {
        await using var h = await BuildAsync();

        await h.Screen.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F7, shift: false, alt: false, control: true));
        var catalog = (ContentModelMusicCatalog)h.ContentModelService.GetModel("LeftPanel")!;

        // The root listing is the two artists and nothing else.
        await h.Screen.HandleKeyEventAsync(Insert());
        await h.Screen.HandleKeyEventAsync(Insert());

        Assert.AreEqual(2, catalog.SelectionCount, "Insert must reach the panel, not the search filter");
        Assert.IsNull(catalog.Filter, "Insert is not a filter character");

        var isHandled = await h.Screen.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.F5, shift: false, alt: false, control: false));

        Assert.IsTrue(isHandled);
        Assert.AreEqual(3, h.Player.Playlist.Count, "Both marked artists' tracks are queued");
        Assert.AreEqual(0, catalog.SelectionCount);

        static KeyEvent Insert() => new('\0', ConsoleKey.Insert, shift: false, alt: false, control: false);
    }

    /// <summary>
    /// Documents why Alt+M cannot be the binding: the panel claims it first for type-to-search.
    /// Any future Alt+letter hot key added to the status bar will hit exactly this.
    /// </summary>
    [TestMethod]
    public async Task AltM_IsSwallowedByThePanelSearchFilter_SoItCannotBeUsedAsAHotKey()
    {
        await using var h = await BuildAsync();

        await h.Screen.HandleKeyEventAsync(new KeyEvent('m', ConsoleKey.M, shift: false, alt: true, control: false));

        Assert.AreEqual("m", h.ContentModelService.GetModel("LeftPanel")!.Filter,
            "The focused panel starts its search filter on Alt+printable");
        Assert.IsInstanceOfType<ContentModelFileSystem>(h.ContentModelService.GetModel("LeftPanel"),
            "Alt+M never reaches the status bar, so it cannot open the music panels");
    }

    /// <summary>
    /// Searching a music drive through the real key router. The models turn a filter into a listing
    /// of their own, but nothing types into them directly — it arrives as Alt+&lt;letter&gt; then
    /// plain letters, from the same type-to-search the file panels use, and Esc ends it.
    /// </summary>
    [TestMethod]
    public async Task AltText_SearchesTheCatalogPanel()
    {
        await using var h = await BuildAsync();
        await h.Screen.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F7, shift: false, alt: false, control: true));

        var catalog = (ContentModelMusicCatalog)h.ContentModelService.GetModel("LeftPanel")!;

        await h.Screen.HandleKeyEventAsync(new KeyEvent('m', ConsoleKey.M, shift: false, alt: true, control: false));
        await h.Screen.HandleKeyEventAsync(new KeyEvent('i', ConsoleKey.I, shift: false, alt: false, control: false));

        Assert.AreEqual("mi", catalog.Filter);
        Assert.IsTrue(catalog.IsSearching);
        Assert.IsTrue(
            catalog.FilteredEntries.OfType<MusicEntry>().Any(row => row.DisplayName.Contains("Miles Davis")),
            "The search reaches rows the panel is not standing in");

        await h.Screen.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        Assert.IsNull(catalog.Filter, "Esc ends the search");
        Assert.IsFalse(catalog.IsSearching);
    }

    [TestMethod]
    public async Task AltText_FiltersThePlaylistPanel()
    {
        await using var h = await BuildAsync();
        await h.Screen.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F7, shift: false, alt: false, control: true));

        var playlist = (ContentModelMusicPlaylist)h.ContentModelService.GetModel("RightPanel")!;
        h.Player.Playlist.AddRange([
            new iTerminal.Core.Data.Music.MusicTrack(@"C:\Music\a.mp3") { Title = "Echoes", Artist = "Pink Floyd" },
            new iTerminal.Core.Data.Music.MusicTrack(@"C:\Music\b.mp3") { Title = "Glory Box", Artist = "Portishead" }]);
        playlist.Refresh();

        // The right panel has to be the focused one for the keys to reach it, exactly as in the app.
        h.RightPanel.SetFocus();

        await h.Screen.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, shift: false, alt: true, control: false));
        await h.Screen.HandleKeyEventAsync(new KeyEvent('c', ConsoleKey.C, shift: false, alt: false, control: false));

        Assert.AreEqual("ec", playlist.Filter);
        Assert.IsTrue(playlist.IsFiltering);

        var tracks = playlist.FilteredEntries.OfType<MusicEntry>().Where(row => row.Kind == MusicEntryKind.Track).ToList();
        Assert.AreEqual(1, tracks.Count, "Only the matching track is listed");
        Assert.AreEqual("Echoes", tracks[0].Track!.Title);

        await h.Screen.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        Assert.IsNull(playlist.Filter);
        Assert.AreEqual(2, playlist.FilteredEntries.OfType<MusicEntry>().Count(row => row.Kind == MusicEntryKind.Track));
    }
}
