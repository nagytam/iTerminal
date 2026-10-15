using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// The playing queue in the database. What matters here is that a queue comes back in the order it
/// went in and with the tags it had — a restored queue that has to re-read tags to draw itself would
/// be no better than the file it replaced.
/// </summary>
[TestClass]
public class MusicPlaylistStoreTest
{
    private string _tempRoot = string.Empty;
    private SqliteDbContextFactory _dbContextFactory = null!;

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
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicQueueTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _dbContextFactory = new SqliteDbContextFactory(Path.Combine(_tempRoot, "queue.sqlite"));
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

    private MusicPlaylistStore CreateStore() => new(_dbContextFactory);

    private static MusicTrack[] ThreeTracks() =>
    [
        new MusicTrack(@"C:\Music\b.mp3") { Title = "Bee", Artist = "Band", Album = "First", Duration = TimeSpan.FromMinutes(3) },
        new MusicTrack(@"C:\Music\a.mp3") { Title = "Ay", Artist = "Band", Album = "First" },
        new MusicTrack(@"C:\Music\c.mp3") { Title = "Sea", Artist = "Other", Album = "Second" },
    ];

    [TestMethod]
    public void Load_WithNothingSaved_ReturnsEmpty()
    {
        Assert.AreEqual(0, CreateStore().Load().Count);
    }

    [TestMethod]
    public void Save_ThenLoad_RestoresTheQueueInOrderWithItsTags()
    {
        CreateStore().Save(ThreeTracks());

        // A second store over the same database is the restart this feature exists for.
        IReadOnlyList<MusicTrack> restored = CreateStore().Load();

        CollectionAssert.AreEqual(
            new[] { @"C:\Music\b.mp3", @"C:\Music\a.mp3", @"C:\Music\c.mp3" },
            restored.Select(track => track.Path).ToArray(),
            "The queue is an ordered list, not a set");

        Assert.AreEqual("Bee", restored[0].Title);
        Assert.AreEqual("Band", restored[0].Artist);
        Assert.AreEqual("First", restored[0].Album);
        Assert.AreEqual(TimeSpan.FromMinutes(3), restored[0].Duration, "A restored queue must not have to re-read tags");
        Assert.AreEqual(TimeSpan.Zero, restored[1].Duration, "An unprobed duration stays unknown rather than becoming zero-ish");
    }

    [TestMethod]
    public void Save_ReplacesTheWholeQueue()
    {
        MusicPlaylistStore store = CreateStore();
        store.Save(ThreeTracks());

        store.Save([new MusicTrack(@"C:\Music\only.mp3") { Title = "Only" }]);

        IReadOnlyList<MusicTrack> restored = store.Load();
        Assert.AreEqual(1, restored.Count, "Yesterday's rows must not survive under today's queue");
        Assert.AreEqual(@"C:\Music\only.mp3", restored[0].Path);
    }

    [TestMethod]
    public void Save_AnEmptyQueue_ClearsTheStoredOne()
    {
        MusicPlaylistStore store = CreateStore();
        store.Save(ThreeTracks());

        store.Save([]);

        Assert.AreEqual(0, CreateStore().Load().Count, "Clearing the playlist has to persist too");
    }

    [TestMethod]
    public void Save_WithoutADatabase_DoesNotThrow()
    {
        // Constructed the way DI would if the database were unavailable.
        MusicPlaylistStore store = new(dbContextFactory: null);

        store.Save(ThreeTracks());

        Assert.AreEqual(3, store.Load().Count);
    }
}
