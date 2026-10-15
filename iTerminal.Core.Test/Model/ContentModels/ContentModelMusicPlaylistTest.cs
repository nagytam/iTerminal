using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;
using iTerminal.Core.Test.Services.Music;
using Moq;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// The playing queue as a panel drive. What is being pinned down here is that the three control
/// folders are reachable by navigation alone — that is the whole point of turning the player into a
/// panel, and it is the part a key-binding test would not notice going missing.
/// </summary>
[TestClass]
public class ContentModelMusicPlaylistTest
{
    private static IFrequentExecutionGuardService ImmediateGuard()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(action => action());
        return guard.Object;
    }

    private static (ContentModelMusicPlaylist Model, MusicPlayerService Player, FakeAudioDeviceService Devices) Create()
    {
        var devices = new FakeAudioDeviceService();
        var player = new MusicPlayerService(devices);
        var model = new ContentModelMusicPlaylist(
            "LeftPanel",
            new SettingsService(),
            player,
            devices,
            folderHistoryService: null,
            ImmediateGuard());

        model.SetUrl(new Url(Container.MUSIC_PLAYLIST));
        return (model, player, devices);
    }

    private static MusicEntry? Row(ContentModelMusicPlaylist model, string displayNameContains) =>
        model.FilteredEntries
            .OfType<MusicEntry>()
            .FirstOrDefault(entry => entry.DisplayName.Contains(displayNameContains, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The panel is created while the app is still starting, before the background jobs have
    /// necessarily run, so it restores the saved session itself rather than waiting to be handed the
    /// queue — otherwise its first paint is an empty list and the queue appears a moment later.
    /// </summary>
    [TestMethod]
    public void Root_ListsTheSavedQueueOnItsFirstPaint()
    {
        var devices = new FakeAudioDeviceService();
        using MusicPlayerService player = new(devices);
        SettingsService settings = new();
        FakeMusicPlaylistStore store = new();
        store.Save([new MusicTrack(@"C:\Music\saved.mp3") { Title = "Saved" }]);

        ContentModelMusicPlaylist model = new(
            "LeftPanel",
            settings,
            player,
            devices,
            folderHistoryService: null,
            ImmediateGuard(),
            new MusicSessionRestoreService(player, store, new PlaylistService(Path.Combine(Path.GetTempPath(), "MusicPlaylistPanelTest_" + Guid.NewGuid().ToString("N")[..8])), settings));

        model.SetUrl(new Url(Container.MUSIC_PLAYLIST));

        Assert.AreEqual(1, player.Playlist.Count, "The panel restored the queue itself");
        Assert.IsNotNull(Row(model, "Saved"), "…and listed it without waiting for a background job");
    }

    /// <summary>An in-memory stand-in for the queue table.</summary>
    private sealed class FakeMusicPlaylistStore : IMusicPlaylistStore
    {
        private List<MusicTrack> _tracks = [];

        public IReadOnlyList<MusicTrack> Load() => [.. _tracks];

        public void Save(IEnumerable<MusicTrack> tracks) => _tracks = [.. tracks];
    }

    /// <summary>
    /// Typing narrows the queue instead of tinting it. A queue is a list to find something in, and
    /// the row wanted is as likely to be off-screen as on it — which is exactly what dimming the
    /// non-matching rows cannot help with.
    /// </summary>
    [TestMethod]
    public void Filter_NarrowsTheQueueToTheMatchingTracks()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([
                new MusicTrack(@"C:\Music\a.mp3") { Title = "Comfortably Numb", Artist = "Pink Floyd", Album = "The Wall" },
                new MusicTrack(@"C:\Music\b.mp3") { Title = "Glory Box", Artist = "Portishead", Album = "Dummy" },
                new MusicTrack(@"C:\Music\c.mp3") { Title = "Echoes", Artist = "Pink Floyd", Album = "Meddle" }]);
            model.Refresh();

            model.Filter = "floyd";

            var tracks = model.FilteredEntries.OfType<MusicEntry>().Where(row => row.Kind == MusicEntryKind.Track).ToList();
            Assert.AreEqual(2, tracks.Count);
            Assert.IsTrue(tracks.All(row => row.Track!.Artist == "Pink Floyd"));
            Assert.IsTrue(model.IsFiltering);
        }
    }

    [TestMethod]
    public void Filter_KeepsTheControlFolders_BecauseTheyAreHowThePanelIsOperated()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.Add(new MusicTrack(@"C:\Music\a.mp3") { Title = "Echoes" });
            model.Refresh();

            model.Filter = "nothing matches this";

            Assert.IsNotNull(Row(model, "[Transport]"));
            Assert.IsNotNull(Row(model, "[Volume]"));
            Assert.IsNotNull(Row(model, "[Output]"));
            Assert.IsFalse(
                model.FilteredEntries.OfType<MusicEntry>().Any(row => row.Kind == MusicEntryKind.Track),
                "…and no track survives a filter nothing matches");
        }
    }

    [TestMethod]
    public void Filter_MatchesArtistAlbumTitleAndFileName_InAnyOrder()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([
                new MusicTrack(@"C:\Music\numb.mp3") { Title = "Comfortably Numb", Artist = "Pink Floyd", Album = "The Wall" },
                new MusicTrack(@"C:\Music\untagged-roadhouse-blues.mp3")]);
            model.Refresh();

            model.Filter = "wall floyd";
            Assert.AreEqual(1, model.FilteredEntries.OfType<MusicEntry>().Count(row => row.Kind == MusicEntryKind.Track));

            model.Filter = "ROADHOUSE";
            Assert.AreEqual(
                1,
                model.FilteredEntries.OfType<MusicEntry>().Count(row => row.Kind == MusicEntryKind.Track),
                "An untagged track has nothing but its file name to be found by");
        }
    }

    /// <summary>
    /// A filtered row still plays the track it stands for. The row keeps its position in the queue,
    /// which is both what the user reads and what Enter acts on.
    /// </summary>
    [TestMethod]
    public void Filter_KeepsEachRowsPlaceInTheQueue()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([
                new MusicTrack(@"C:\Music\a.mp3") { Title = "One" },
                new MusicTrack(@"C:\Music\b.mp3") { Title = "Two" },
                new MusicTrack(@"C:\Music\c.mp3") { Title = "Three" }]);
            model.Refresh();

            model.Filter = "Three";

            var row = model.FilteredEntries.OfType<MusicEntry>().First(entry => entry.Kind == MusicEntryKind.Track);
            Assert.AreEqual(2, row.PlaylistIndex, "The third track is still the third track");
            StringAssert.Contains(row.DisplayName, "3.");
        }
    }

    [TestMethod]
    public void Filter_IsReportedInTheHeaderAndTheFooter()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([
                new MusicTrack(@"C:\Music\a.mp3") { Title = "Echoes" },
                new MusicTrack(@"C:\Music\b.mp3") { Title = "Glory Box" }]);
            model.Refresh();

            StringAssert.Contains(model.GetFooter(), "Alt+<text> filters the queue");

            model.Filter = "echo";

            StringAssert.Contains(model.GetHeader(80), "Playlist \"echo\" (1 of 2)");
            StringAssert.Contains(model.GetFooter(), "1 of 2 tracks");
            StringAssert.Contains(model.GetFooter(), "Esc clears the filter");
        }
    }

    [TestMethod]
    public void ClearingTheFilter_BringsTheWholeQueueBack()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([
                new MusicTrack(@"C:\Music\a.mp3") { Title = "Echoes" },
                new MusicTrack(@"C:\Music\b.mp3") { Title = "Glory Box" }]);
            model.Refresh();
            model.Filter = "echo";

            model.Filter = null;

            Assert.IsFalse(model.IsFiltering);
            Assert.AreEqual(2, model.FilteredEntries.OfType<MusicEntry>().Count(row => row.Kind == MusicEntryKind.Track));
        }
    }

    /// <summary>
    /// Both music drives build their listing *from* the filter, so one left set across a navigation
    /// would have the panel draw the old hits under the new URL.
    /// </summary>
    [TestMethod]
    public void NavigatingIntoAControlFolder_EndsTheFilter()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.Add(new MusicTrack(@"C:\Music\a.mp3") { Title = "Echoes" });
            model.Refresh();
            model.Filter = "echo";

            model.SetUrl(Row(model, "[Volume]")!.Url);

            Assert.IsNull(model.Filter);
            Assert.IsNotNull(Row(model, "Louder"));
        }
    }

    [TestMethod]
    public void Root_ListsTheControlFoldersBeforeTheTracks()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([new MusicTrack(@"C:\Music\a.mp3"), new MusicTrack(@"C:\Music\b.mp3")]);
            model.Refresh();

            var rows = model.FilteredEntries.OfType<MusicEntry>().ToList();

            Assert.AreEqual(5, rows.Count, "Three control folders plus the two tracks");
            CollectionAssert.AreEqual(
                new[] { MusicEntryKind.Folder, MusicEntryKind.Folder, MusicEntryKind.Folder, MusicEntryKind.Track, MusicEntryKind.Track },
                rows.Select(row => row.Kind).ToArray());
        }
    }

    [TestMethod]
    public void ControlFolders_AreNavigableUrlsOnTheSameDrive()
    {
        var (model, player, _) = Create();
        using (player)
        {
            foreach (var label in new[] { "[Transport]", "[Volume]", "[Output]" })
            {
                var folder = Row(model, label);
                Assert.IsNotNull(folder, $"{label} must be listed at the root");
                Assert.AreEqual(PathType.Folder, folder!.Url.PathType, $"{label} must be enterable");
                Assert.AreEqual(ContainerType.MusicPlaylist, folder.Url.ContainerType);

                model.SetUrl(folder.Url);
                Assert.IsTrue(model.FilteredEntries.Count > 1, $"{label} must list something to act on");
                Assert.AreEqual("..", model.FilteredEntries[0].DisplayName, "Every control folder needs a way back");

                model.SetUrl(new Url(Container.MUSIC_PLAYLIST));
            }
        }
    }

    [TestMethod]
    public void ActivatingATransportRow_DrivesThePlayer()
    {
        var (model, player, _) = Create();
        using (player)
        {
            model.SetUrl(new Url(Container.MUSIC_PLAYLIST + "transport/"));

            var shuffle = Row(model, "Shuffle");
            Assert.IsNotNull(shuffle);
            Assert.IsFalse(player.IsShuffled);

            model.Activate(shuffle!);

            Assert.IsTrue(player.IsShuffled, "Enter on the shuffle row must toggle shuffle");
            Assert.IsNotNull(Row(model, "Shuffle"), "The folder must still be listed after the action");
        }
    }

    [TestMethod]
    public void ActivatingAVolumeRow_SetsTheVolume()
    {
        var (model, player, _) = Create();
        using (player)
        {
            model.SetUrl(new Url(Container.MUSIC_PLAYLIST + "volume/"));

            var preset = model.FilteredEntries
                .OfType<MusicEntry>()
                .First(entry => entry.VolumeLevel == 25);

            model.Activate(preset);

            Assert.AreEqual(25, player.Volume);
        }
    }

    [TestMethod]
    public void ActivatingADeviceRow_SelectsThatOutput()
    {
        var (model, player, _) = Create();
        using (player)
        {
            model.SetUrl(new Url(Container.MUSIC_PLAYLIST + "devices/"));

            var headphones = model.FilteredEntries
                .OfType<MusicEntry>()
                .First(entry => entry.Kind == MusicEntryKind.Device && entry.DisplayName.Contains("Headphones"));

            model.Activate(headphones);

            Assert.AreEqual("2", player.DeviceId);
        }
    }

    [TestMethod]
    public void RemoveFromPlaylist_DropsTheTrackWithoutTouchingTheFile()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([new MusicTrack(@"C:\Music\a.mp3"), new MusicTrack(@"C:\Music\b.mp3")]);
            model.Refresh();

            var track = model.FilteredEntries.OfType<MusicEntry>().First(entry => entry.Kind == MusicEntryKind.Track);

            Assert.IsTrue(model.RemoveFromPlaylist(track));
            Assert.AreEqual(1, player.Playlist.Count);
        }
    }

    [TestMethod]
    public void Header_ReportsWhatIsCuedAndFitsTheWidth()
    {
        var (model, player, _) = Create();
        using (player)
        {
            // Nothing loaded: the count and nothing else — no glyph, no times, no bar, and no label
            // saying so either.
            Assert.AreEqual("Playlist (0)", model.GetHeader(60));

            player.Playlist.AddRange([new MusicTrack(@"C:\Music\a.mp3") { Title = "Song", Artist = "Band" }]);
            player.Playlist.CurrentIndex = 0;

            var header = model.GetHeader(60);

            StringAssert.Contains(header, "Band - Song");
            Assert.IsTrue(header.Length <= 60, "The header must never overrun the border it is drawn into");
        }
    }

    [TestMethod]
    public void Footer_CarriesTheVolumeRepeatShuffleAndOutput()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Volume = 40;

            var footer = model.GetFooter();

            StringAssert.Contains(footer, "Vol 40%");
            StringAssert.Contains(footer, "Repeat off");
            StringAssert.Contains(footer, "Shuffle off");
        }
    }

    /// <summary>
    /// End to end over the real engine: when playback moves to the next song by itself, the panel's
    /// highlight has to move with it. This is the panel half of
    /// <c>MusicPlayerServiceTest.NaturalEnd_AnnouncesTheTrackItMovedTo</c> — the engine announcing
    /// the advance is worth nothing if the rows are not rebuilt from it.
    /// </summary>
    [TestMethod]
    public void MovingToTheNextSong_MovesTheHighlightToIt()
    {
        var (model, player, devices) = Create();
        using (player)
        {
            var root = Path.Combine(Path.GetTempPath(), "PlaylistHighlightTest_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            try
            {
                // Long enough that neither track can end on its own while the assertions run: what is
                // under test is the advance, not a race with the end of the queue.
                player.Playlist.AddRange(
                [
                    new MusicTrack(TestAudioFiles.WriteSilence(root, "a.wav", TimeSpan.FromMinutes(1))),
                    new MusicTrack(TestAudioFiles.WriteSilence(root, "b.wav", TimeSpan.FromMinutes(1))),
                ]);
                devices.WriteDelay = TimeSpan.FromMilliseconds(20);

                player.PlayIndex(0);
                Assert.IsTrue(WaitFor(() => CurrentTrackRow(model) == 0), "the first track to be highlighted");

                player.Next();

                Assert.IsTrue(
                    WaitFor(() => CurrentTrackRow(model) == 1),
                    "The highlight must follow playback onto the second track");
            }
            finally
            {
                player.Stop();
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    /// <summary>Playlist position of the row the model currently marks as playing, or -1.</summary>
    private static int CurrentTrackRow(ContentModelMusicPlaylist model) =>
        model.FilteredEntries
            .OfType<MusicEntry>()
            .Where(entry => entry.Kind == MusicEntryKind.Track && entry.IsCurrent)
            .Select(entry => entry.PlaylistIndex)
            .DefaultIfEmpty(-1)
            .First();

    private static bool WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(15);
        }

        return false;
    }

    /// <summary>
    /// The model subscribes to a singleton that outlives it, so a replaced model has to let go or it
    /// keeps repainting a panel that no longer exists.
    /// </summary>
    [TestMethod]
    public void Dispose_UnsubscribesFromThePlayer()
    {
        var (model, player, _) = Create();
        using (player)
        {
            player.Playlist.AddRange([new MusicTrack(@"C:\Music\a.mp3")]);
            model.Refresh();
            var countBeforeDispose = model.FilteredEntries.Count;

            model.Dispose();
            player.Playlist.AddRange([new MusicTrack(@"C:\Music\b.mp3")]);
            player.Volume = 11; // raises StateChanged

            Assert.AreEqual(countBeforeDispose, model.FilteredEntries.Count,
                "A disposed model must not rebuild itself from the player's events");
        }
    }
}
