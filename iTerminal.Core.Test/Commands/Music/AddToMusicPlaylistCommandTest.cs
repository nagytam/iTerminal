using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Commands.Music;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Services.Music;
using Moq;

namespace iTerminal.Core.Test.Commands.Music;

/// <summary>
/// F5 from the catalog panel to the playlist panel — the reason the player is two drives rather than
/// one dialog. The command has to claim F5 only for that pairing, so that copying an indexed file
/// out to a real folder still reaches the ordinary copy dialog.
/// </summary>
[TestClass]
public class AddToMusicPlaylistCommandTest
{
    private sealed record Harness(
        ContentModelService ContentModelService,
        ContentModelMusicCatalog Catalog,
        ContentModelMusicPlaylist? Playlist,
        MusicPlayerService Player) : IDisposable
    {
        public void Dispose() => Player.Dispose();
    }

    private static Harness Build(bool rightPanelIsPlaylist = true)
    {
        var settingsService = new SettingsService();
        var devices = new FakeAudioDeviceService();
        var player = new MusicPlayerService(devices);
        var library = new FakeMusicLibraryService()
            .Add("The Beatles", "Abbey Road", "Come Together")
            .Add("The Beatles", "Abbey Road", "Something")
            .Add("Miles Davis", "Kind of Blue", "So What");

        var contentModelService = new ContentModelService(
            new TaskManager(), settingsService, new FileSystemService(),
            Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(), Mock.Of<IIPhoneFileSystemService>(),
            Mock.Of<INetworkProfileService>(), Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            new FolderHistoryService(settingsService),
            ImmediateGuard(), new MemoryCacheService(), Serilog.Core.Logger.None,
            dbContextFactory: null, stopwatchFactory: null,
            musicPlayerService: player, musicLibraryService: library, audioDeviceService: devices);

        var catalog = (ContentModelMusicCatalog)contentModelService
            .AddContentModel(ContentModelService.Left, new Url(Container.MUSIC_CATALOG))!;

        var right = contentModelService.AddContentModel(
            ContentModelService.Right,
            rightPanelIsPlaylist ? new Url(Container.MUSIC_PLAYLIST) : new Url(Directory.GetCurrentDirectory()))!;

        contentModelService.FocusedPanel = ContentModelService.Left;

        return new Harness(contentModelService, catalog, right as ContentModelMusicPlaylist, player);
    }

    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        return guard.Object;
    }

    private static AddToMusicPlaylistCommand CommandFor(Harness harness) =>
        new(harness.Catalog, harness.ContentModelService);

    [TestMethod]
    public void Key_IsF5_SoItReadsAsCopyingToTheOtherPanel()
    {
        using var harness = Build();

        var command = CommandFor(harness);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F5, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Control || command.Key.Shift || command.Key.Alt);
    }

    [TestMethod]
    public async Task F5_OnAnArtistRow_QueuesTheWholeCatalogueInTheOtherPanel()
    {
        using var harness = Build();
        SelectArtistRow(harness);

        var isExecuted = await CommandFor(harness).RunAsync(null);

        Assert.IsTrue(isExecuted);
        Assert.AreEqual(2, harness.Player.Playlist.Count);
        Assert.IsTrue(
            harness.Playlist!.FilteredEntries.OfType<MusicEntry>().Any(entry => entry.Kind == MusicEntryKind.Track),
            "The playlist panel must show the tracks that just arrived");
    }

    [TestMethod]
    public void CanExecute_IsFalseWhenTheOtherPanelIsNotThePlaylist()
    {
        using var harness = Build(rightPanelIsPlaylist: false);
        SelectArtistRow(harness);

        Assert.IsFalse(
            CommandFor(harness).CanExecute(),
            "Without a playlist panel to copy into, F5 must fall through to the ordinary copy dialog");
    }

    [TestMethod]
    public void CanExecute_IsFalseWhenTheCatalogPanelIsNotFocused()
    {
        using var harness = Build();
        SelectArtistRow(harness);
        harness.ContentModelService.FocusedPanel = ContentModelService.Right;

        Assert.IsFalse(
            CommandFor(harness).CanExecute(),
            "A control-tree walk offers the key to every panel; only the focused one may act on it");
    }

    private static void SelectArtistRow(Harness harness, string artist = "The Beatles")
    {
        var index = harness.Catalog.FilteredEntries
            .OfType<MusicEntry>()
            .ToList()
            .FindIndex(entry => entry.Kind == MusicEntryKind.Folder && entry.DisplayName == artist);

        Assert.IsTrue(index >= 0, $"Precondition: the catalog must list \"{artist}\"");
        harness.Catalog.CursorPosition.Y = index;
    }

    [TestMethod]
    public async Task F5_WithMarkedRows_QueuesAllOfThem()
    {
        using var harness = Build();

        SelectArtistRow(harness, "Miles Davis");
        harness.Catalog.ToggleSelectionAtCursor();
        SelectArtistRow(harness, "The Beatles");
        harness.Catalog.ToggleSelectionAtCursor();

        Assert.AreEqual(2, harness.Catalog.SelectionCount, "Precondition: two artists are marked");

        var isExecuted = await CommandFor(harness).RunAsync(null);

        Assert.IsTrue(isExecuted);
        Assert.AreEqual(3, harness.Player.Playlist.Count, "Both artists' catalogues are queued");
        Assert.AreEqual(0, harness.Catalog.SelectionCount, "The operation consumes the marks");
    }

    [TestMethod]
    public async Task F5_WithMarkedRows_IgnoresWhereTheCursorHappensToBe()
    {
        using var harness = Build();

        SelectArtistRow(harness, "Miles Davis");
        harness.Catalog.ToggleSelectionAtCursor();

        // Marked one artist, then wandered onto the other. The mark is the instruction.
        SelectArtistRow(harness, "The Beatles");

        await CommandFor(harness).RunAsync(null);

        Assert.AreEqual(1, harness.Player.Playlist.Count);
        Assert.AreEqual("So What", harness.Player.Playlist.Tracks[0].Title);
    }

    [TestMethod]
    public void CanExecute_IsTrueWhenRowsAreMarkedEvenIfTheCursorIsOnAnUnmarkableRow()
    {
        using var harness = Build();

        // An album inside an artist, so there is a ".." row to park the cursor on — the one kind of
        // row that stands for no tracks at all.
        harness.Catalog.SetUrl(new Url(Container.MUSIC_CATALOG + "The%20Beatles/"));
        harness.Catalog.CursorPosition.Y = 1;
        harness.Catalog.ToggleSelectionAtCursor();
        harness.Catalog.CursorPosition.Y = 0;

        Assert.AreEqual(1, harness.Catalog.SelectionCount, "Precondition: an album is marked");
        Assert.IsTrue(CommandFor(harness).CanExecute());
    }
}
