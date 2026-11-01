using iTerminal.Core.Data.Music;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Services.Music;

namespace iTerminal.Core.Test.Jobs;

/// <summary>
/// The job that makes the player's session survive a restart. The queue is the interesting half: it
/// lives in the database now, and the file older versions wrote has to be picked up exactly once so
/// an upgrade does not silently empty the queue.
/// </summary>
[TestClass]
public class MusicSessionJobTest
{
    private string _tempRoot = string.Empty;

    /// <summary>An in-memory stand-in for the database table, so no SQLite file is needed here.</summary>
    private sealed class FakeMusicPlaylistStore : IMusicPlaylistStore
    {
        private List<MusicTrack> _tracks = [];

        public int SaveCount { get; private set; }

        public IReadOnlyList<MusicTrack> Load() => [.. _tracks];

        public void Save(IEnumerable<MusicTrack> tracks)
        {
            SaveCount++;
            _tracks = [.. tracks];
        }
    }

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicSessionJobTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
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

    private (MusicSessionJob Job, MusicPlayerService Player, FakeMusicPlaylistStore Store, PlaylistService Files) Build()
    {
        var player = new MusicPlayerService(new FakeAudioDeviceService());
        var store = new FakeMusicPlaylistStore();
        var files = new PlaylistService(Path.Combine(_tempRoot, "playlists"));
        var settings = new SettingsService();
        var job = new MusicSessionJob(
            player,
            store,
            new MusicSessionRestoreService(player, store, files, settings),
            settings)
        {
            PollInterval = TimeSpan.FromMilliseconds(20),
        };
        return (job, player, store, files);
    }

    private static async Task RunUntilAsync(MusicSessionJob job, Func<bool> condition, string because)
    {
        using var cancellation = new CancellationTokenSource();
        var run = job.ExecuteAsync(cancellation.Token);

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && !condition())
        {
            await Task.Delay(20);
        }

        await cancellation.CancelAsync();
        await run;

        Assert.IsTrue(condition(), $"Timed out waiting for: {because}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Restore_LoadsTheQueueFromTheDatabase()
    {
        var (job, player, store, _) = Build();
        using (player)
        {
            store.Save([new MusicTrack(@"C:\Music\a.mp3") { Title = "A" }, new MusicTrack(@"C:\Music\b.mp3")]);

            await RunUntilAsync(job, () => player.Playlist.Count == 2, "the stored queue to be restored");

            Assert.AreEqual("A", player.Playlist[0]!.Title);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Restore_ImportsThePreDatabaseQueueFileWhenTheTableIsEmpty()
    {
        var (job, player, store, files) = Build();
        using (player)
        {
            files.Save(files.LastPlaylistPath, [new MusicTrack(@"C:\Music\legacy.mp3") { Title = "Legacy" }]);

            await RunUntilAsync(job, () => player.Playlist.Count == 1, "the legacy queue file to be imported");

            Assert.AreEqual("Legacy", player.Playlist[0]!.Title);
            Assert.AreEqual(1, store.Load().Count, "…and written into the database, so it is read from there next time");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Restore_PrefersTheDatabaseOverTheLegacyFile()
    {
        var (job, player, store, files) = Build();
        using (player)
        {
            store.Save([new MusicTrack(@"C:\Music\current.mp3")]);
            files.Save(files.LastPlaylistPath, [new MusicTrack(@"C:\Music\stale.mp3"), new MusicTrack(@"C:\Music\stale2.mp3")]);

            await RunUntilAsync(job, () => player.Playlist.Count == 1, "the database queue to win");

            Assert.AreEqual(@"C:\Music\current.mp3", player.Playlist[0]!.Path);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingTheQueue_PersistsIt()
    {
        var (job, player, store, _) = Build();
        using (player)
        {
            // Seeded so the restore is observable: only once the playlist has been replaced from the
            // store is it certain the job has captured its baseline version, and a change made
            // before that would look like no change at all.
            store.Save([new MusicTrack(@"C:\Music\a.mp3")]);

            using var cancellation = new CancellationTokenSource();
            var run = job.ExecuteAsync(cancellation.Token);

            await WaitAsync(() => player.Playlist.Count == 1, "the stored queue to be restored");

            player.Playlist.Add(new MusicTrack(@"C:\Music\b.mp3"));

            var persisted = await WaitAsync(() => store.Load().Count == 2, "the new queue to be written out");

            await cancellation.CancelAsync();
            await run;

            Assert.IsTrue(persisted);
            Assert.AreEqual(@"C:\Music\b.mp3", store.Load()[1].Path);
        }
    }

    /// <summary>
    /// Which track is cued moves on its own — every time one ends — while the queue it indexes into
    /// does not change at all, so it is watched alongside the volume and not alongside the queue.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task CuingAnotherTrack_PersistsIt()
    {
        var player = new MusicPlayerService(new FakeAudioDeviceService());
        using (player)
        {
            var store = new FakeMusicPlaylistStore();
            var files = new PlaylistService(Path.Combine(_tempRoot, "playlists"));
            var settings = new SettingsService();
            var job = new MusicSessionJob(
                player,
                store,
                new MusicSessionRestoreService(player, store, files, settings),
                settings)
            {
                PollInterval = TimeSpan.FromMilliseconds(20),
            };

            store.Save([new MusicTrack(@"C:\Music\a.mp3"), new MusicTrack(@"C:\Music\b.mp3")]);

            using var cancellation = new CancellationTokenSource();
            var run = job.ExecuteAsync(cancellation.Token);

            await WaitAsync(() => player.Playlist.Count == 2, "the stored queue to be restored");

            player.Playlist.CurrentIndex = 1;

            var persisted = await WaitAsync(
                () => settings.Get(iTerminal.Core.Data.Settings.MusicCurrentIndex) == "1",
                "the cued track to be written out");

            await cancellation.CancelAsync();
            await run;

            Assert.IsTrue(persisted);
        }
    }

    private static async Task<bool> WaitAsync(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Timed out waiting for: {because}");
        return false;
    }
}
