using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// Putting the saved session back into the player. The restore is shared between the session job and
/// the playlist panel because either of them can be first — the panel is built while the app is still
/// coming up, before the jobs have necessarily started — and it must happen exactly once whoever wins.
/// </summary>
[TestClass]
public class MusicSessionRestoreServiceTest
{
    private string _tempRoot = string.Empty;

    private sealed class FakeMusicPlaylistStore : IMusicPlaylistStore
    {
        private List<MusicTrack> _tracks = [];

        public int LoadCount { get; private set; }

        public IReadOnlyList<MusicTrack> Load()
        {
            LoadCount++;
            return [.. _tracks];
        }

        public void Save(IEnumerable<MusicTrack> tracks) => _tracks = [.. tracks];
    }

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicSessionRestoreTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private (MusicSessionRestoreService Restore, MusicPlayerService Player, FakeMusicPlaylistStore Store, SettingsService Settings) Build()
    {
        MusicPlayerService player = new(new FakeAudioDeviceService());
        FakeMusicPlaylistStore store = new();
        PlaylistService files = new(Path.Combine(_tempRoot, "playlists"));
        SettingsService settings = new();
        return (new MusicSessionRestoreService(player, store, files, settings), player, store, settings);
    }

    [TestMethod]
    public void RestoreOnce_PutsTheSavedQueueBackIntoThePlayer()
    {
        var (restore, player, store, _) = Build();
        using (player)
        {
            store.Save([new MusicTrack(@"C:\Music\a.mp3") { Title = "A" }, new MusicTrack(@"C:\Music\b.mp3")]);

            Assert.IsTrue(restore.RestoreOnce());

            Assert.AreEqual(2, player.Playlist.Count);
            Assert.AreEqual("A", player.Playlist[0]!.Title);
        }
    }

    [TestMethod]
    public void RestoreOnce_RaisesStateChanged_SoAnAlreadyOpenPanelRedraws()
    {
        var (restore, player, store, _) = Build();
        using (player)
        {
            store.Save([new MusicTrack(@"C:\Music\a.mp3")]);

            // The playlist is a plain data type with no events; a panel watches the player. Without
            // this the queue would be in memory and the panel would still be drawing an empty list.
            int changes = 0;
            player.StateChanged += (_, _) => Interlocked.Increment(ref changes);

            restore.RestoreOnce();

            Assert.IsTrue(changes > 0);
        }
    }

    /// <summary>
    /// The track that was playing comes back cued — named in the panel header, marked in the list —
    /// but stopped. Restoring a session shows where the user was; it does not start making noise.
    /// </summary>
    [TestMethod]
    public void RestoreOnce_CuesTheTrackThatWasPlaying_WithoutStartingIt()
    {
        var (restore, player, store, settings) = Build();
        using (player)
        {
            store.Save([
                new MusicTrack(@"C:\Music\a.mp3") { Title = "A" },
                new MusicTrack(@"C:\Music\b.mp3") { Title = "B" },
                new MusicTrack(@"C:\Music\c.mp3") { Title = "C" }]);
            settings.Set(Settings.MusicCurrentIndex, "1");

            restore.RestoreOnce();

            Assert.AreEqual(1, player.Playlist.CurrentIndex);
            Assert.AreEqual("B", player.CurrentTrack?.Title);
            Assert.AreEqual(PlaybackState.Stopped, player.State, "A restored session is cued, not playing");
        }
    }

    [TestMethod]
    public void RestoreOnce_WithNoSavedTrack_CuesNothing()
    {
        var (restore, player, store, _) = Build();
        using (player)
        {
            store.Save([new MusicTrack(@"C:\Music\a.mp3")]);

            restore.RestoreOnce();

            Assert.AreEqual(-1, player.Playlist.CurrentIndex, "An absent setting must not cue the first track");
            Assert.IsNull(player.CurrentTrack);
        }
    }

    [TestMethod]
    public void RestoreOnce_WithACueThatNoLongerFitsTheQueue_CuesNothing()
    {
        var (restore, player, store, settings) = Build();
        using (player)
        {
            store.Save([new MusicTrack(@"C:\Music\a.mp3")]);
            settings.Set(Settings.MusicCurrentIndex, "7");

            restore.RestoreOnce();

            Assert.AreEqual(-1, player.Playlist.CurrentIndex);
        }
    }

    [TestMethod]
    public void RestoreOnce_RestoresTheTransportSettings()
    {
        var (restore, player, _, settings) = Build();
        using (player)
        {
            settings.Set(Settings.MusicVolume, "42");
            settings.Set(Settings.MusicRepeatMode, nameof(RepeatMode.All));
            settings.Set(Settings.MusicShuffle, "True");

            restore.RestoreOnce();

            Assert.AreEqual(42, player.Volume);
            Assert.AreEqual(RepeatMode.All, player.RepeatMode);
            Assert.IsTrue(player.IsShuffled);
        }
    }

    /// <summary>
    /// The queue is read from the table once; after that memory is the truth. Otherwise clearing the
    /// queue and reopening the panel before the next save would hand the old queue straight back.
    /// </summary>
    [TestMethod]
    public void RestoreOnce_IsExactlyOnce()
    {
        var (restore, player, store, _) = Build();
        using (player)
        {
            store.Save([new MusicTrack(@"C:\Music\a.mp3")]);

            Assert.IsTrue(restore.RestoreOnce(), "The first caller does the work");
            Assert.AreEqual(1, store.LoadCount);

            player.Playlist.Clear();

            Assert.IsFalse(restore.RestoreOnce(), "The second finds it already done");
            Assert.AreEqual(1, store.LoadCount);
            Assert.AreEqual(0, player.Playlist.Count, "A queue the user cleared stays cleared");
        }
    }
}
