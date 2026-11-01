using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;
using iTerminal.Core.Test.Services.Music;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// The indexed library as a panel drive: artist / album / track by navigation, and the whole library
/// by search. The search half is the one worth guarding — it hangs off the panel's ordinary
/// type-to-search filter, so an innocuous change to how filters work would silently turn library
/// search back into row dimming.
/// </summary>
[TestClass]
public class ContentModelMusicCatalogTest
{
    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        return guard.Object;
    }

    private static (ContentModelMusicCatalog Model, FakeMusicLibraryService Library, MusicPlayerService Player) Create()
    {
        var library = new FakeMusicLibraryService()
            .Add("The Beatles", "Abbey Road", "Come Together")
            .Add("The Beatles", "Abbey Road", "Something")
            .Add("The Beatles", "Revolver", "Taxman")
            .Add("Miles Davis", "Kind of Blue", "So What");

        var player = new MusicPlayerService(new FakeAudioDeviceService());
        var model = new ContentModelMusicCatalog(
            "LeftPanel",
            new SettingsService(),
            library,
            player,
            folderHistoryService: null,
            ImmediateGuard());

        model.SetUrl(new Url(Container.MUSIC_CATALOG));
        return (model, library, player);
    }

    private static IReadOnlyList<MusicEntry> Rows(ContentModelMusicCatalog model) =>
        [.. model.FilteredEntries.OfType<MusicEntry>()];

    [Timeout(60_000)]
    [TestMethod]
    public void Root_ListsOneFolderPerArtistAndNothingElse()
    {
        var (model, _, player) = Create();
        using (player)
        {
            // The indexing state belongs to the title and the footer; a row repeating it would cost
            // the listing a line on every panel-full of the catalog.
            CollectionAssert.AreEqual(
                new[] { "Miles Davis", "The Beatles" },
                Rows(model).Select(row => row.DisplayName).ToArray());
        }
    }

    /// <summary>
    /// The panel over a real, already-populated SQLite index — no scan in this test at all. This is
    /// what "the catalog is shown from the database" has to mean in practice: the rows a previous
    /// run left in the table are on screen the moment the panel opens.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task Root_ListsWhatIsInTheDatabase_WithoutScanningAnything()
    {
        string root = Path.Combine(Path.GetTempPath(), "MusicCatalogDbTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        try
        {
            SqliteDbContextFactory factory = new(Path.Combine(root, "library.sqlite"));
            SettingsService settings = new();

            // A scan over a folder of real files, then thrown away — everything after this point
            // reaches the tracks through the table alone.
            settings.Set(Settings.MusicFolder, WriteLibrary(root));
            await new MusicLibraryService(factory, settings).RefreshAsync();

            MusicLibraryService library = new(factory, settings);
            using MusicPlayerService player = new(new FakeAudioDeviceService());
            ContentModelMusicCatalog model = new("LeftPanel", settings, library, player, null, ImmediateGuard());
            model.SetUrl(new Url(Container.MUSIC_CATALOG));

            CollectionAssert.AreEqual(
                new[] { "Miles Davis", "The Beatles" },
                model.FilteredEntries.OfType<MusicEntry>().Select(row => row.DisplayName).ToArray());
            StringAssert.Contains(model.GetHeader(80), "3 tracks");
            Assert.IsFalse(library.IsIndexing, "Nothing was scanned to produce that listing");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>Three tagged files under a music folder, and the folder's path.</summary>
    private static string WriteLibrary(string root)
    {
        string music = Path.Combine(root, "Music");
        Directory.CreateDirectory(music);
        TestAudioFiles.WriteId3V2File(music, "together.mp3", "Come Together", "The Beatles", "Abbey Road");
        TestAudioFiles.WriteId3V2File(music, "something.mp3", "Something", "The Beatles", "Abbey Road");
        TestAudioFiles.WriteId3V2File(music, "sowhat.mp3", "So What", "Miles Davis", "Kind of Blue");
        return music;
    }

    private sealed class SqliteDbContextFactory : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options;

        public SqliteDbContextFactory(string databasePath)
        {
            DbContextOptionsBuilder<iTerminalDbContext> builder = new();
            builder.UseSqlite($"Data Source={databasePath}");
            _options = builder.Options;
        }

        public iTerminalDbContext CreateDbContext() => new(_options);

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    /// <summary>
    /// A scan publishes its rows in batches as it reads them, and the progress report is the only
    /// event a panel gets until the whole scan lands — so the listing has to follow that, or a first
    /// scan of a large folder would leave the catalog empty for minutes at a time.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void Root_PicksUpEntriesPublishedWhileAScanIsStillRunning()
    {
        var (model, library, player) = Create();
        using (player)
        {
            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 10, indexed: 4);
            Assert.IsFalse(Rows(model).Any(row => row.DisplayName == "Portishead"));

            library.Add("Portishead", "Dummy", "Glory Box");
            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 10, indexed: 5);

            Assert.IsTrue(
                Rows(model).Any(row => row.DisplayName == "Portishead"),
                "Rows indexed mid-scan must reach the panel before the scan finishes");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Root_KeepsListingOnlyArtists_WhileAScanIsRunning()
    {
        var (model, library, player) = Create();
        using (player)
        {
            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 300, indexed: 150);

            Assert.IsFalse(
                Rows(model).Any(row => row.Kind == MusicEntryKind.Action),
                "Progress goes in the header and the footer, never in the listing");
            StringAssert.Contains(model.GetHeader(200), "Indexing 150/300");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NavigatingAnArtist_ThenAnAlbum_ReachesTheTracks()
    {
        var (model, _, player) = Create();
        using (player)
        {
            var beatles = Rows(model).First(row => row.DisplayName == "The Beatles");
            model.SetUrl(beatles.Url);

            CollectionAssert.AreEqual(
                new[] { "..", "Abbey Road", "Revolver" },
                Rows(model).Select(row => row.DisplayName).ToArray());

            var abbeyRoad = Rows(model).First(row => row.DisplayName == "Abbey Road");
            model.SetUrl(abbeyRoad.Url);

            var tracks = Rows(model).Where(row => row.Kind == MusicEntryKind.Track).ToList();
            Assert.AreEqual(2, tracks.Count);
            Assert.IsTrue(tracks.All(track => track.Track is not null));
        }
    }

    /// <summary>
    /// Names with a slash in them are the reason the segments are percent-escaped; without that,
    /// "AC/DC" would look like an album inside an artist called "AC".
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ArtistNameWithASlash_StillNavigatesAsOneLevel()
    {
        var library = new FakeMusicLibraryService().Add("AC/DC", "Back in Black", "Hells Bells");
        using var player = new MusicPlayerService(new FakeAudioDeviceService());
        var model = new ContentModelMusicCatalog("LeftPanel", new SettingsService(), library, player, null, ImmediateGuard());
        model.SetUrl(new Url(Container.MUSIC_CATALOG));

        var artist = Rows(model).First(row => row.Kind == MusicEntryKind.Folder);
        Assert.AreEqual("AC/DC", artist.DisplayName);

        model.SetUrl(artist.Url);

        CollectionAssert.AreEqual(
            new[] { "..", "Back in Black" },
            Rows(model).Select(row => row.DisplayName).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SettingTheFilter_ReplacesTheListingWithLibraryWideHits()
    {
        var (model, _, player) = Create();
        using (player)
        {
            var beatles = Rows(model).First(row => row.DisplayName == "The Beatles");
            model.SetUrl(beatles.Url);

            // A track that is not in the folder the panel is standing in.
            model.Filter = "So What";

            Assert.IsTrue(model.IsSearching);
            Assert.IsTrue(
                Rows(model).Any(row => row.DisplayName.Contains("So What")),
                "Search must reach outside the current folder, unlike a plain row filter");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ClearingTheFilter_RestoresTheFolderListing()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.Filter = "Miles";
            Assert.IsTrue(model.IsSearching);

            model.Filter = null;

            Assert.IsFalse(model.IsSearching);
            Assert.IsTrue(Rows(model).Any(row => row.DisplayName == "The Beatles"));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryListedRowCountsAsAMatch_SoSearchHitsAreNotDimmed()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.Filter = "Beatles";

            Assert.IsTrue(Rows(model).All(model.IsEntryMatch));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddToPlaylist_ExpandsAnArtistRowIntoEveryTrack()
    {
        var (model, _, player) = Create();
        using (player)
        {
            var beatles = Rows(model).First(row => row.DisplayName == "The Beatles");

            var added = model.AddToPlaylist(beatles);

            Assert.AreEqual(3, added, "An artist row must queue the whole catalogue in one go");
            Assert.AreEqual(3, player.Playlist.Count);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddToPlaylist_SkipsTracksAlreadyQueued()
    {
        var (model, _, player) = Create();
        using (player)
        {
            var beatles = Rows(model).First(row => row.DisplayName == "The Beatles");
            model.AddToPlaylist(beatles);

            var added = model.AddToPlaylist(beatles);

            Assert.AreEqual(0, added);
            Assert.AreEqual(3, player.Playlist.Count);
            StringAssert.Contains(model.StatusMessage, "already in the playlist");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ActivatingATrackRow_QueuesIt()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.Filter = "So What";
            var track = Rows(model).First(row => row.Kind == MusicEntryKind.Track);

            model.Activate(track);

            Assert.AreEqual(1, player.Playlist.Count);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HeaderAndFooter_ShowTheIndexingState()
    {
        var (model, library, player) = Create();
        using (player)
        {
            StringAssert.Contains(model.GetHeader(80), "4 tracks");
            Assert.IsFalse(model.GetFooter().Contains("indexing"), "Not indexing means no spinner");

            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 40, indexed: 12);

            StringAssert.Contains(model.GetHeader(200), "Indexing 12/40 (30%)");
            StringAssert.Contains(model.GetFooter(), "indexing 12/40 (30%)");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Title_KeepsTheProgressIntactWhenThePanelIsNarrow()
    {
        var (model, library, player) = Create();
        using (player)
        {
            model.SetUrl(new Url(Container.MUSIC_CATALOG + "A%20Very%20Long%20Artist%20Name%20Indeed/"));
            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 812, indexed: 40);

            // Half of an 80-column terminal. The path is what gives way; the counts are the reason
            // the title is worth reading at all.
            var title = model.GetHeader(37);

            Assert.IsTrue(title.Length <= 37, $"\"{title}\" does not fit the panel");
            StringAssert.Contains(title, "Indexing 40/812 (4%)");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Title_ShowsTheDiscoveryStageBeforeAnyTotalIsKnown()
    {
        var (model, library, player) = Create();
        using (player)
        {
            library.SetProgress(MusicIndexingPhase.Discovering, discovered: 300);

            StringAssert.Contains(model.GetHeader(60), "Discovering… 300 audio files");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HeaderAndFooter_ShowDiscoveryAsItsOwnStage()
    {
        var (model, library, player) = Create();
        using (player)
        {
            library.SetProgress(MusicIndexingPhase.Discovering, discovered: 812);

            // Discovery has no total to count against, so it may only report what it has found.
            StringAssert.Contains(model.GetHeader(200), "Discovering… 812 audio files");
            StringAssert.Contains(model.GetFooter(), "discovering… 812 audio files found");
            Assert.IsFalse(model.GetFooter().Contains("812/"), "Discovery must not pretend to know a total");

            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 812, indexed: 40);

            // And once it is done, the same line says so and switches to "n of m".
            StringAssert.Contains(model.GetFooter(), "812 audio files discovered");
            StringAssert.Contains(model.GetFooter(), "indexing 40/812 (4%)");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HeaderAndFooter_TrackBothStagesWithoutALibraryChange()
    {
        var (model, library, player) = Create();
        using (player)
        {
            // A scan raises nothing on LibraryChanged until it lands, so the header and footer can
            // only follow the progress event.
            library.SetProgress(MusicIndexingPhase.Discovering, discovered: 300);

            StringAssert.Contains(model.GetHeader(200), "Discovering… 300 audio files");

            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 300, indexed: 150);

            StringAssert.Contains(model.GetHeader(200), "Indexing 150/300 (50%)");

            library.SetProgress(MusicIndexingPhase.Idle);

            StringAssert.Contains(model.GetHeader(200), "4 tracks", "Back to the track count once the scan is over");
            StringAssert.Contains(model.GetFooter(), "Ctrl+R re-indexes", "The key that starts a scan is named somewhere");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ForcedRefresh_KicksOffAReindex()
    {
        var (model, library, player) = Create();
        using (player)
        {
            model.Refresh(forceRefresh: true);

            // The scan is deliberately off-thread; awaiting the task it started keeps the assertion
            // deterministic instead of racing a thread pool that the rest of the suite is using.
            Assert.IsNotNull(model.ReindexTaskForTest, "Ctrl+R must start a scan");
            await model.ReindexTaskForTest!;

            Assert.AreEqual(1, library.RefreshCount);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Insert_MarksTheRowAndStepsDown()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.CursorPosition.Y = 0; // the first artist

            Assert.IsTrue(model.ToggleSelectionAtCursor());

            Assert.AreEqual(1, model.SelectionCount);
            Assert.AreEqual("Miles Davis", model.SelectedEntries[0].DisplayName);
            Assert.AreEqual(1u, model.CursorPosition.Y, "Marking steps down so a run can be marked by holding Ins");

            model.CursorPosition.Y = 0;
            Assert.IsTrue(model.ToggleSelectionAtCursor());
            Assert.AreEqual(0, model.SelectionCount, "Ins on a marked row unmarks it");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Insert_OnARowThatStandsForNoTracks_StepsOverIt()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.SetUrl(new Url(Container.MUSIC_CATALOG + "The%20Beatles/"));
            model.CursorPosition.Y = 0; // the ".." row

            Assert.IsFalse(model.ToggleSelectionAtCursor());

            Assert.AreEqual(0, model.SelectionCount);
            Assert.AreEqual(1u, model.CursorPosition.Y, "The cursor still advances, so holding Ins is not blocked");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Marks_SurviveARepaint()
    {
        var (model, library, player) = Create();
        using (player)
        {
            model.CursorPosition.Y = 0;
            model.ToggleSelectionAtCursor();

            // The listing is rebuilt several times a second while a scan runs. Marks are held by the
            // model, not by the row objects, precisely so that this does not drop them.
            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 100, indexed: 20);

            Assert.AreEqual(1, model.SelectionCount);
            Assert.IsTrue(model.SelectedEntries[0].IsSelected, "The rebuilt row still carries the mark");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NavigatingAway_ClearsTheMarks()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.CursorPosition.Y = 0;
            model.ToggleSelectionAtCursor();

            model.SetUrl(new Url(Container.MUSIC_CATALOG + "The%20Beatles/"));

            Assert.AreEqual(0, model.SelectionCount, "Marks belong to the listing they were made in");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SearchingClearsTheMarks_SoF5CannotAddInvisibleRows()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.CursorPosition.Y = 0;
            model.ToggleSelectionAtCursor();

            model.Filter = "Abbey";

            Assert.AreEqual(0, model.SelectionCount);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Footer_SaysHowManyRowsAreMarked()
    {
        var (model, _, player) = Create();
        using (player)
        {
            StringAssert.Contains(model.GetFooter(), "Ins marks a row");

            model.CursorPosition.Y = 0;
            model.ToggleSelectionAtCursor();

            StringAssert.Contains(model.GetFooter(), "1 marked");
            StringAssert.Contains(model.GetFooter(), "F5 adds them to the playlist");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AddSelectionToPlaylist_QueuesEveryMarkedRowAndClearsTheMarks()
    {
        var (model, _, player) = Create();
        using (player)
        {
            model.CursorPosition.Y = 0;
            model.ToggleSelectionAtCursor(); // Miles Davis — 1 track
            model.ToggleSelectionAtCursor(); // The Beatles — 3 tracks

            Assert.AreEqual(2, model.SelectionCount);
            Assert.AreEqual(4, model.AddSelectionToPlaylist());
            Assert.AreEqual(0, model.SelectionCount);
            StringAssert.Contains(model.GetFooter(), "Added 4 tracks from 2 marked rows");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dispose_UnsubscribesFromTheLibrary()
    {
        var (model, library, player) = Create();
        using (player)
        {
            model.Dispose();
            library.Add("New Artist", "New Album", "New Track");
            library.RaiseLibraryChanged();

            Assert.IsFalse(
                Rows(model).Any(row => row.DisplayName == "New Artist"),
                "A disposed model must not rebuild itself from the library's events");

            // The progress event is the model's other subscription, and rebuilding on it would pick
            // up the new artist just the same.
            library.SetProgress(MusicIndexingPhase.Indexing, discovered: 5, indexed: 1);

            Assert.IsFalse(
                Rows(model).Any(row => row.DisplayName == "New Artist"),
                "Nor from its progress events");
        }
    }
}
