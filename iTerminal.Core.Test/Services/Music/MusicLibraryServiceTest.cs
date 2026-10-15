using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Music;

[TestClass]
public class MusicLibraryServiceTest
{
    private string _tempRoot = string.Empty;
    private string _musicFolder = string.Empty;
    private string _databasePath = string.Empty;
    private SqliteDbContextFactory _dbContextFactory = null!;
    private SettingsService _settingsService = null!;

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

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicLibraryTest_" + Guid.NewGuid().ToString("N")[..8]);
        _musicFolder = Path.Combine(_tempRoot, "Music");
        Directory.CreateDirectory(_musicFolder);

        _databasePath = Path.Combine(_tempRoot, "library.sqlite");
        _dbContextFactory = new SqliteDbContextFactory(_databasePath);

        _settingsService = new SettingsService();
        _settingsService.Set(Settings.MusicFolder, _musicFolder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    private MusicLibraryService CreateService() => new(_dbContextFactory, _settingsService);

    /// <summary>Writes a tagged MP3 into a sub-folder of the music folder and returns its path.</summary>
    private string WriteTrack(string relativeFolder, string fileName, string title, string artist, string album)
    {
        string folder = Path.Combine(_musicFolder, relativeFolder);
        Directory.CreateDirectory(folder);
        return TestAudioFiles.WriteId3V2File(folder, fileName, title, artist, album);
    }

    [TestMethod]
    public void MusicFolder_FallsBackToTheSystemMusicFolderWhenNothingIsConfigured()
    {
        SettingsService empty = new();

        string expected = OperatingSystem.IsLinux()
            ? "/home/tamas/nata/music"
            : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        Assert.AreEqual(expected, MusicLibraryService.ResolveMusicFolder(empty));
    }

    [TestMethod]
    public void MusicFolder_UsesTheConfiguredSetting()
    {
        Assert.AreEqual(_musicFolder, CreateService().MusicFolder);
    }

    [TestMethod]
    public async Task RefreshAsync_IndexesEveryAudioFileWithItsTags()
    {
        WriteTrack("Pink Floyd", "numb.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        WriteTrack("Pink Floyd", "bricks.mp3", "Another Brick", "Pink Floyd", "The Wall");
        File.WriteAllText(Path.Combine(_musicFolder, "notes.txt"), "not audio");

        MusicLibraryService service = CreateService();
        int count = await service.RefreshAsync();

        Assert.AreEqual(2, count, "Only the audio files are indexed");
        Assert.AreEqual(2, service.TrackCount);

        MusicLibraryEntry entry = service.Entries.First(e => e.Path.EndsWith("numb.mp3", StringComparison.Ordinal));
        Assert.AreEqual("Comfortably Numb", entry.Title);
        Assert.AreEqual("Pink Floyd", entry.Artist);
        Assert.AreEqual("The Wall", entry.Album);
    }

    [TestMethod]
    public async Task RefreshAsync_MissingFolder_ReportsItRatherThanThrowing()
    {
        _settingsService.Set(Settings.MusicFolder, Path.Combine(_tempRoot, "does-not-exist"));
        MusicLibraryService service = CreateService();

        int count = await service.RefreshAsync();

        Assert.AreEqual(0, count);
        StringAssert.Contains(service.StatusMessage, "not found");
    }

    [TestMethod]
    public async Task RefreshAsync_DropsRowsWhoseFileIsGone()
    {
        string path = WriteTrack("Artist", "one.mp3", "One", "Artist", "Album");
        MusicLibraryService service = CreateService();
        await service.RefreshAsync();
        Assert.AreEqual(1, service.TrackCount);

        File.Delete(path);
        int count = await service.RefreshAsync();

        Assert.AreEqual(0, count);
        Assert.AreEqual(0, service.TrackCount);
    }

    [TestMethod]
    public async Task RefreshAsync_CachesMetadataInTheDatabaseForTheNextRun()
    {
        WriteTrack("Metallica", "one.mp3", "One", "Metallica", "And Justice For All");
        await CreateService().RefreshAsync();

        // A second service instance shares only the database, so anything it can see was cached.
        MusicLibraryService reloaded = CreateService();

        Assert.AreEqual(1, reloaded.TrackCount);
        Assert.AreEqual("Metallica", reloaded.Entries[0].Artist);
        Assert.IsNotNull(reloaded.LastIndexedUtc);
    }

    [TestMethod]
    public async Task RefreshAsync_KeepsUnchangedRowsAndPicksUpEditedTags()
    {
        string path = WriteTrack("Artist", "song.mp3", "Old Title", "Artist", "Album");
        MusicLibraryService service = CreateService();
        await service.RefreshAsync();
        Assert.AreEqual("Old Title", service.Entries[0].Title);

        // Re-tagging changes both the size and the timestamp, which is exactly the change stamp the
        // scan compares against.
        File.Delete(path);
        WriteTrack("Artist", "song.mp3", "New Title Entirely", "Artist", "Album");

        await service.RefreshAsync();

        Assert.AreEqual(1, service.TrackCount);
        Assert.AreEqual("New Title Entirely", service.Entries[0].Title);
    }

    [TestMethod]
    public async Task Search_EmptyQuery_ReturnsNothing()
    {
        WriteTrack("Artist", "song.mp3", "Song", "Artist", "Album");
        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.AreEqual(0, service.Search(string.Empty).Count);
        Assert.AreEqual(0, service.Search("   ").Count);
    }

    [TestMethod]
    public async Task Search_MatchesArtistsAlbumsAndTracks()
    {
        WriteTrack("Pink Floyd", "numb.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        WriteTrack("Pink Floyd", "bricks.mp3", "Another Brick In The Wall", "Pink Floyd", "The Wall");
        WriteTrack("Portishead", "glory.mp3", "Glory Box", "Portishead", "Dummy");

        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        IReadOnlyList<MusicSearchResult> results = service.Search("floyd");

        MusicSearchResult artist = results.Single(r => r.Kind == MusicSearchResultKind.Artist);
        Assert.AreEqual("Pink Floyd", artist.Artist);
        Assert.AreEqual(2, artist.TrackCount);

        MusicSearchResult album = results.Single(r => r.Kind == MusicSearchResultKind.Album);
        Assert.AreEqual("The Wall", album.Album);
        Assert.AreEqual(2, album.TrackCount);

        Assert.AreEqual(2, results.Count(r => r.Kind == MusicSearchResultKind.Track));
        Assert.IsFalse(results.Any(r => r.Artist == "Portishead"));
    }

    [TestMethod]
    public async Task Search_ListsArtistsAndAlbumsBeforeIndividualTracks()
    {
        WriteTrack("Pink Floyd", "numb.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        IReadOnlyList<MusicSearchResult> results = service.Search("floyd");

        Assert.AreEqual(MusicSearchResultKind.Artist, results[0].Kind);
        Assert.AreEqual(MusicSearchResultKind.Album, results[1].Kind);
        Assert.AreEqual(MusicSearchResultKind.Track, results[2].Kind);
    }

    [TestMethod]
    public async Task Search_RequiresEveryToken()
    {
        WriteTrack("Pink Floyd", "numb.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        WriteTrack("Portishead", "glory.mp3", "Glory Box", "Portishead", "Dummy");

        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.AreEqual(0, service.Search("floyd dummy").Count, "No entry carries both tokens");
        Assert.IsTrue(service.Search("floyd numb").Any(r => r.Kind == MusicSearchResultKind.Track));
    }

    [TestMethod]
    public async Task Search_IsCaseInsensitiveAndMatchesTheFileNameToo()
    {
        WriteTrack("Untagged", "roadhouse-blues.mp3", string.Empty, string.Empty, string.Empty);
        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.IsTrue(service.Search("ROADHOUSE").Any(r => r.Kind == MusicSearchResultKind.Track));
    }

    [TestMethod]
    public async Task Search_HonoursTheLimit()
    {
        for (int i = 0; i < 12; i++)
        {
            WriteTrack("Artist", $"song{i}.mp3", $"Song {i}", "Artist", "Album");
        }

        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        Assert.AreEqual(5, service.Search("song", limit: 5).Count);
    }

    [TestMethod]
    public async Task ExpandResult_Artist_ReturnsEveryTrackOfThatArtist()
    {
        WriteTrack("Pink Floyd", "numb.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        WriteTrack("Pink Floyd", "echoes.mp3", "Echoes", "Pink Floyd", "Meddle");
        WriteTrack("Portishead", "glory.mp3", "Glory Box", "Portishead", "Dummy");

        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        MusicSearchResult artist = service.Search("floyd").First(r => r.Kind == MusicSearchResultKind.Artist);
        IReadOnlyList<MusicTrack> tracks = service.ExpandResult(artist);

        Assert.AreEqual(2, tracks.Count);
        Assert.IsTrue(tracks.All(t => t.Artist == "Pink Floyd"));
    }

    [TestMethod]
    public async Task ExpandResult_Album_ReturnsOnlyThatAlbum()
    {
        WriteTrack("Pink Floyd", "numb.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        WriteTrack("Pink Floyd", "echoes.mp3", "Echoes", "Pink Floyd", "Meddle");

        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        MusicSearchResult album = service.Search("meddle").First(r => r.Kind == MusicSearchResultKind.Album);
        IReadOnlyList<MusicTrack> tracks = service.ExpandResult(album);

        Assert.AreEqual(1, tracks.Count);
        Assert.AreEqual("Echoes", tracks[0].Title);
    }

    [TestMethod]
    public async Task ExpandResult_Track_ReturnsJustThatTrack()
    {
        WriteTrack("Pink Floyd", "numb.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        MusicLibraryService service = CreateService();
        await service.RefreshAsync();

        MusicSearchResult track = service.Search("numb").First(r => r.Kind == MusicSearchResultKind.Track);

        Assert.AreEqual(1, service.ExpandResult(track).Count);
    }

    [TestMethod]
    public async Task LibraryChanged_FiresOnlyWhenTheIndexActuallyMoved()
    {
        WriteTrack("Artist", "song.mp3", "Song", "Artist", "Album");
        MusicLibraryService service = CreateService();

        int changes = 0;
        service.LibraryChanged += (_, _) => Interlocked.Increment(ref changes);

        await service.RefreshAsync();
        Assert.AreEqual(1, changes, "The first scan indexes a new file");

        await service.RefreshAsync();
        Assert.AreEqual(1, changes, "A second scan over an unchanged folder changes nothing");
    }

    [TestMethod]
    public async Task RefreshAsync_ReportsDiscoveryAndIndexingAsSeparateStages()
    {
        WriteTrack("Artist", "one.mp3", "One", "Artist", "Album");
        WriteTrack("Artist", "two.mp3", "Two", "Artist", "Album");
        WriteTrack("Other", "three.mp3", "Three", "Other", "Album");

        // No rate limit, so the assertions see every step instead of whatever a 250 ms window
        // happened to let through.
        MusicLibraryService service = new(_dbContextFactory, _settingsService)
        {
            ProgressReportInterval = TimeSpan.Zero,
        };

        List<MusicIndexingProgress> reported = [];
        service.IndexingProgressChanged += (_, progress) =>
        {
            lock (reported)
            {
                reported.Add(progress);
            }
        };

        await service.RefreshAsync();

        Assert.IsTrue(
            reported.Any(p => p.Phase == MusicIndexingPhase.Discovering),
            "The folder walk is reported as its own stage");

        MusicIndexingProgress firstIndexing = reported.First(p => p.Phase == MusicIndexingPhase.Indexing);
        Assert.IsTrue(firstIndexing.IsDiscoveryComplete, "Entering the indexing stage means discovery finished");
        Assert.AreEqual(3, firstIndexing.DiscoveredFiles, "Discovery hands over a final total");
        Assert.AreEqual(0, firstIndexing.IndexedFiles, "…before a single file has been indexed");

        Assert.IsFalse(
            reported.Any(p => p.Phase == MusicIndexingPhase.Discovering && p.IsDiscoveryComplete),
            "Discovery cannot claim a total it has not finished counting");

        MusicIndexingProgress last = reported.Last(p => p.Phase == MusicIndexingPhase.Indexing);
        Assert.AreEqual(3, last.IndexedFiles, "The run reaches its total");
        Assert.AreEqual(3, last.DiscoveredFiles);
        Assert.AreEqual(100, last.Percent);

        Assert.AreEqual(MusicIndexingPhase.Idle, reported[^1].Phase, "The scan reports its own end");
        Assert.AreEqual(MusicIndexingProgress.Idle, service.Progress);
        Assert.IsFalse(service.IsIndexing);
    }

    [TestMethod]
    public async Task RefreshAsync_IndexedCountNeverPassesTheDiscoveredTotal()
    {
        for (int i = 0; i < 5; i++)
        {
            WriteTrack("Artist", $"track{i}.mp3", $"Track {i}", "Artist", "Album");
        }

        MusicLibraryService service = new(_dbContextFactory, _settingsService)
        {
            ProgressReportInterval = TimeSpan.Zero,
        };

        List<MusicIndexingProgress> reported = [];
        service.IndexingProgressChanged += (_, progress) =>
        {
            lock (reported)
            {
                reported.Add(progress);
            }
        };

        await service.RefreshAsync();

        foreach (MusicIndexingProgress progress in reported.Where(p => p.Phase == MusicIndexingPhase.Indexing))
        {
            Assert.IsTrue(
                progress.IndexedFiles <= progress.DiscoveredFiles,
                $"{progress.IndexedFiles}/{progress.DiscoveredFiles} is not a progress report anyone can read");
            Assert.IsTrue(progress.Percent is >= 0 and <= 100);
        }
    }

    [TestMethod]
    public async Task RefreshAsync_DiscoveredCountIsAudioFilesOnly()
    {
        WriteTrack("Artist", "song.mp3", "Song", "Artist", "Album");

        // What a real music folder is mostly made of: an artwork cache and a library database, in
        // this case 50 files that must not appear in a denominator the indexer can never reach.
        string cache = Path.Combine(_musicFolder, "iTunes", "Album Artwork", "Cache");
        Directory.CreateDirectory(cache);
        for (int i = 0; i < 50; i++)
        {
            File.WriteAllText(Path.Combine(cache, $"artwork{i}.itc2"), "not audio");
        }

        File.WriteAllText(Path.Combine(_musicFolder, "iTunes Library.itl"), "not audio");

        MusicLibraryService service = new(_dbContextFactory, _settingsService)
        {
            ProgressReportInterval = TimeSpan.Zero,
        };

        List<MusicIndexingProgress> reported = [];
        service.IndexingProgressChanged += (_, progress) =>
        {
            lock (reported)
            {
                reported.Add(progress);
            }
        };

        int count = await service.RefreshAsync();

        Assert.AreEqual(1, count);
        Assert.AreEqual(
            1,
            reported.First(p => p.Phase == MusicIndexingPhase.Indexing).DiscoveredFiles,
            "Discovery counts what will be indexed, not what is in the folder");
    }

    [TestMethod]
    public void Enumerate_KeepsWhatItReadBeforeAFailure()
    {
        // Directory enumeration throws while it is being iterated, not when it is started, so an
        // unreadable entry half way through a folder must not discard the entries already found —
        // that is how a scan silently under-reports what a library contains.
        IReadOnlyList<string> found = MusicLibraryService.Enumerate(FailsHalfWay);

        CollectionAssert.AreEqual(new[] { "one.mp3", "two.mp3" }, found.ToArray());

        static IEnumerable<string> FailsHalfWay()
        {
            yield return "one.mp3";
            yield return "two.mp3";
            throw new UnauthorizedAccessException("the third entry is protected");
        }
    }

    [TestMethod]
    public void Enumerate_LetsCancellationThrough()
    {
        Assert.ThrowsExactly<OperationCanceledException>(() => MusicLibraryService.Enumerate(Cancelled));

        static IEnumerable<string> Cancelled()
        {
            yield return "one.mp3";
            throw new OperationCanceledException();
        }
    }

    [TestMethod]
    public async Task Progress_IsIdleBetweenScans()
    {
        WriteTrack("Artist", "song.mp3", "Song", "Artist", "Album");
        MusicLibraryService service = CreateService();

        Assert.AreEqual(MusicIndexingProgress.Idle, service.Progress, "Nothing has run yet");

        await service.RefreshAsync();

        Assert.AreEqual(MusicIndexingProgress.Idle, service.Progress);
        Assert.IsFalse(service.Progress.IsRunning);
    }

    /// <summary>Rows in the table, read through a connection the scan does not own.</summary>
    private int PersistedRowCount()
    {
        using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();
        return context.MusicLibraryEntries.AsNoTracking().Count();
    }

    /// <summary>A scan flushing one file at a time, so every step is observable.</summary>
    private MusicLibraryService CreateStepwiseService() => new(_dbContextFactory, _settingsService)
    {
        ProgressReportInterval = TimeSpan.Zero,
        ScanFlushBatchSize = 1,
    };

    [TestMethod]
    public async Task RefreshAsync_WritesToTheDatabaseWhileItIsStillScanning()
    {
        for (int i = 0; i < 4; i++)
        {
            WriteTrack("Artist", $"track{i}.mp3", $"Track {i}", "Artist", "Album");
        }

        MusicLibraryService service = CreateStepwiseService();

        // Sampled from the progress event, which the scan raises on its own thread between files —
        // so what this sees is genuinely mid-scan, not the state the run ended in.
        int persistedMidScan = 0;
        service.IndexingProgressChanged += (_, progress) =>
        {
            if (progress.Phase == MusicIndexingPhase.Indexing && progress.IndexedFiles == 3)
            {
                persistedMidScan = PersistedRowCount();
            }
        };

        await service.RefreshAsync();

        Assert.IsTrue(
            persistedMidScan > 0,
            "A scan interrupted half way must leave what it had already indexed in the table");
        Assert.AreEqual(4, PersistedRowCount(), "…and everything by the time it finishes");
    }

    [TestMethod]
    public async Task RefreshAsync_PublishesEntriesAsItGoes_SoThePanelFillsInDuringAScan()
    {
        for (int i = 0; i < 4; i++)
        {
            WriteTrack("Artist", $"track{i}.mp3", $"Track {i}", "Artist", "Album");
        }

        MusicLibraryService service = CreateStepwiseService();

        int countMidScan = 0;
        service.IndexingProgressChanged += (_, progress) =>
        {
            if (progress.Phase == MusicIndexingPhase.Indexing && progress.IndexedFiles == 3)
            {
                countMidScan = service.TrackCount;
            }
        };

        await service.RefreshAsync();

        Assert.IsTrue(
            countMidScan is > 0 and < 4,
            $"The catalog must grow during a first scan, not appear at the end of it (saw {countMidScan})");
        Assert.AreEqual(4, service.TrackCount);
    }

    [TestMethod]
    public async Task RefreshAsync_NeverShrinksTheListingWhileItRuns()
    {
        WriteTrack("Artist", "kept.mp3", "Kept", "Artist", "Album");
        string doomed = WriteTrack("Artist", "gone.mp3", "Gone", "Artist", "Album");

        MusicLibraryService service = CreateStepwiseService();
        await service.RefreshAsync();
        Assert.AreEqual(2, service.TrackCount);

        // One file removed, one added: the removal may only land once the walk is over, because
        // until then the scan does not know the file is gone rather than merely not reached yet.
        File.Delete(doomed);
        WriteTrack("Artist", "new.mp3", "New", "Artist", "Album");

        List<int> counts = [];
        service.IndexingProgressChanged += (_, progress) =>
        {
            if (progress.Phase == MusicIndexingPhase.Indexing)
            {
                counts.Add(service.TrackCount);
            }
        };

        await service.RefreshAsync();

        Assert.IsTrue(counts.All(count => count >= 2), "A running scan must not blank rows already on screen");
        Assert.AreEqual(2, service.TrackCount, "The deleted file is dropped once the scan lands");
        Assert.AreEqual(2, PersistedRowCount());
    }

    /// <summary>Fails the first N contexts it is asked for, then behaves like the real factory.</summary>
    private sealed class FlakyDbContextFactory(IDbContextFactory<iTerminalDbContext> inner, int failures)
        : IDbContextFactory<iTerminalDbContext>
    {
        private int _remainingFailures = failures;

        public iTerminalDbContext CreateDbContext()
        {
            if (_remainingFailures-- > 0)
            {
                throw new InvalidOperationException("the database is busy");
            }

            return inner.CreateDbContext();
        }

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    [TestMethod]
    public async Task Entries_AreLoadedOnALaterAttemptWhenTheFirstDatabaseReadFails()
    {
        WriteTrack("Artist", "song.mp3", "Song", "Artist", "Album");
        await CreateService().RefreshAsync();

        // The database is unavailable exactly once — being recreated at start-up, or held by a
        // second instance. A read that gave up permanently would leave the catalog empty for the
        // rest of the session, with nothing left to trigger another look.
        MusicLibraryService service = new(
            new FlakyDbContextFactory(_dbContextFactory, failures: 1),
            _settingsService)
        {
            LoadRetryInterval = TimeSpan.Zero,
        };

        Assert.AreEqual(0, service.TrackCount, "Nothing could be read yet");
        Assert.AreEqual(1, service.TrackCount, "The next read finds the table again");
        Assert.AreEqual("Song", service.Entries[0].Title);
    }

    [TestMethod]
    public async Task Entries_AreReadFromTheTableOnceAndNotOnEveryAccess()
    {
        WriteTrack("Artist", "song.mp3", "Song", "Artist", "Album");
        await CreateService().RefreshAsync();

        // One context for the successful load and no more: the catalog panel touches this on every
        // repaint, several times a second while a scan runs.
        CountingDbContextFactory counting = new(_dbContextFactory);
        MusicLibraryService service = new(counting, _settingsService);

        _ = service.TrackCount;
        _ = service.Entries;
        _ = service.Search("song");

        Assert.AreEqual(1, counting.Count);
    }

    private sealed class CountingDbContextFactory(IDbContextFactory<iTerminalDbContext> inner)
        : IDbContextFactory<iTerminalDbContext>
    {
        public int Count { get; private set; }

        public iTerminalDbContext CreateDbContext()
        {
            Count++;
            return inner.CreateDbContext();
        }

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
