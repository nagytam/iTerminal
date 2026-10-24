using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Dialogs.Music;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;
using iTerminal.Core.Test.Services.Music;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Music;

/// <summary>
/// The F3 viewer for audio files. Two things are worth pinning down: that the file's own words
/// (artist, title, tags) reach the screen, and that the transport drives the one shared player
/// rather than a private one — the viewer must never start a second stream over the music panels.
/// </summary>
[TestClass]
public class MusicFileViewerDialogTest
{
    /// <summary>
    /// Per-write pause for the fake device, so the fake track plays at something like real time
    /// instead of being consumed in microseconds. The playback tests also repeat the track, which
    /// makes "playing" a state that stays put long enough to observe on a loaded machine.
    /// </summary>
    private static readonly TimeSpan PlaybackPacing = TimeSpan.FromMilliseconds(100);

    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicViewerTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static (MusicFileViewerDialog Dialog, MusicPlayerService Player, FakeAudioDeviceService Devices)
        CreateDialog(string path)
    {
        var devices = new FakeAudioDeviceService();
        var player = new MusicPlayerService(devices);
        var dialog = new MusicFileViewerDialog(player, devices, new MusicMetadataService(), Mock.Of<iConsole.Services.IBeepService>(), Mock.Of<iConsole.Services.IDateTimeProviderService>(), CreateThemeService())
        {
            FilePath = path,
        };

        return (dialog, player, devices);
    }

    private static async Task<Control> ShowAsync(MusicFileViewerDialog dialog)
    {
        var root = new Control(CreateThemeService()) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        await dialog.LoadTask;
        Assert.IsFalse(dialog.IsLoading, "the viewer should be done reading once its load task completes");
        return root;
    }

    /// <summary>
    /// Polls a transport state. Playback runs on the player's own thread, so the state cannot be
    /// awaited; the timeout is generous because the whole test assembly runs in parallel and a
    /// loaded machine can delay this loop by seconds.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> condition, string what, int timeoutMs = 30000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    private static string Render(MusicFileViewerDialog dialog)
    {
        var buffer = new iConsole.Test.Core.MockBuffer(new Size(120, 40));
        dialog.Draw(buffer);
        return buffer.ContentAsString;
    }

    private static KeyEvent Key(char keyChar, ConsoleKey consoleKey, bool shift = false) =>
        new(keyChar, consoleKey, shift, false, false);

    [TestMethod]
    public async Task Show_ReadsTheFilesTagsInTheBackground()
    {
        var path = TestAudioFiles.WriteId3V2File(_tempRoot, "song.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");
        var (dialog, player, _) = CreateDialog(path);
        using (player)
        {
            await ShowAsync(dialog);

            Assert.AreEqual("Pink Floyd", dialog.Metadata.Artist);
            Assert.AreEqual("Comfortably Numb", dialog.Metadata.Title);
            Assert.AreEqual("The Wall", dialog.Metadata.Album);
        }
    }

    [TestMethod]
    public async Task Draw_ShowsArtistTitleAlbumTheTagsAndTheTransport()
    {
        var path = TestAudioFiles.WriteId3V2FileWithFrames(
            _tempRoot,
            "song.mp3",
            ("TIT2", "Teardrop"),
            ("TPE1", "Massive Attack"),
            ("TALB", "Mezzanine"),
            ("TRCK", "5"));
        var (dialog, player, _) = CreateDialog(path);
        using (player)
        {
            await ShowAsync(dialog);
            dialog.OnInitializeLayout();

            var screen = Render(dialog);

            StringAssert.Contains(screen, "Massive Attack", "the artist belongs on screen");
            StringAssert.Contains(screen, "Teardrop");
            StringAssert.Contains(screen, "Mezzanine");
            StringAssert.Contains(screen, "Tags (ID3v2.3)", "the tag section names where the tags came from");
            StringAssert.Contains(screen, "Player", "the media control needs a home");
            StringAssert.Contains(screen, "Space:play/pause", "the transport keys have to be discoverable");
            StringAssert.Contains(screen, "Esc:close");
            StringAssert.Contains(screen, "Volume");
        }
    }

    [TestMethod]
    public async Task Draw_ListsEveryTagInTheTagList()
    {
        var path = TestAudioFiles.WriteId3V2FileWithFrames(
            _tempRoot,
            "song.mp3",
            ("TIT2", "Roygbiv"),
            ("TPUB", "Warp Records"));
        var (dialog, player, _) = CreateDialog(path);
        using (player)
        {
            await ShowAsync(dialog);
            dialog.OnInitializeLayout();

            var screen = Render(dialog);

            StringAssert.Contains(screen, "Publisher");
            StringAssert.Contains(screen, "Warp Records");
        }
    }

    [TestMethod]
    public async Task Space_QueuesTheFileAndPlaysItThroughTheSharedPlayer()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "silence.wav", TimeSpan.FromSeconds(3), sampleRate: 8000);
        var (dialog, player, devices) = CreateDialog(path);
        devices.WriteDelay = PlaybackPacing;
        player.RepeatMode = RepeatMode.One;
        using (player)
        {
            await ShowAsync(dialog);

            var handled = await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar));

            Assert.IsTrue(handled);
            Assert.AreEqual(1, player.Playlist.Count, "the file is queued so the one shared player can play it");
            Assert.AreEqual(path, player.Playlist.Tracks[0].Path);
            await WaitForAsync(() => player.State == PlaybackState.Playing, "playback to start");
        }
    }

    [TestMethod]
    public async Task Space_Twice_PausesTheFileItStarted()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "silence.wav", TimeSpan.FromSeconds(3), sampleRate: 8000);
        var (dialog, player, devices) = CreateDialog(path);
        devices.WriteDelay = PlaybackPacing;
        player.RepeatMode = RepeatMode.One;
        using (player)
        {
            await ShowAsync(dialog);
            await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar));
            await WaitForAsync(() => player.State == PlaybackState.Playing, "playback to start");

            await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar));

            await WaitForAsync(() => player.State == PlaybackState.Paused, "playback to pause");
            Assert.AreEqual(1, player.Playlist.Count, "pausing must not queue the file a second time");
        }
    }

    [TestMethod]
    public async Task S_StopsPlayback()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "silence.wav", TimeSpan.FromSeconds(3), sampleRate: 8000);
        var (dialog, player, devices) = CreateDialog(path);
        devices.WriteDelay = PlaybackPacing;
        player.RepeatMode = RepeatMode.One;
        using (player)
        {
            await ShowAsync(dialog);
            await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar));
            await WaitForAsync(() => player.State == PlaybackState.Playing, "playback to start");

            await dialog.HandleKeyEventAsync(Key('s', ConsoleKey.S));

            await WaitForAsync(() => player.State == PlaybackState.Stopped, "playback to stop");
        }
    }

    [TestMethod]
    public async Task PlusAndMinus_ChangeTheSharedVolume()
    {
        var path = TestAudioFiles.WriteId3V2File(_tempRoot, "song.mp3", "Time", "Pink Floyd", "Dark Side");
        var (dialog, player, _) = CreateDialog(path);
        using (player)
        {
            await ShowAsync(dialog);
            player.Volume = 50;

            await dialog.HandleKeyEventAsync(Key('+', ConsoleKey.Add));
            Assert.AreEqual(55, player.Volume);

            await dialog.HandleKeyEventAsync(Key('-', ConsoleKey.Subtract));
            await dialog.HandleKeyEventAsync(Key('-', ConsoleKey.Subtract));
            Assert.AreEqual(45, player.Volume);
        }
    }

    [TestMethod]
    public async Task Escape_ClosesTheDialogButLeavesTheMusicPlaying()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "silence.wav", TimeSpan.FromSeconds(3), sampleRate: 8000);
        var (dialog, player, devices) = CreateDialog(path);
        devices.WriteDelay = PlaybackPacing;
        player.RepeatMode = RepeatMode.One;
        using (player)
        {
            var root = await ShowAsync(dialog);
            await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar));
            await WaitForAsync(() => player.State == PlaybackState.Playing, "playback to start");

            await dialog.HandleKeyEventAsync(Key('\u001b', ConsoleKey.Escape));

            Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "Esc closes the viewer");
            Assert.AreEqual(PlaybackState.Playing, player.State, "closing a viewer never stops the music");
        }
    }

    [TestMethod]
    public async Task Playing_AFileThePlayerCannotDecode_ExplainsInsteadOfQueueingIt()
    {
        var path = Path.Combine(_tempRoot, "track.opus");
        File.WriteAllBytes(path, new byte[256]);

        var (dialog, player, _) = CreateDialog(path);
        using (player)
        {
            await ShowAsync(dialog);
            if (dialog.Metadata.CanPlay)
            {
                Assert.Inconclusive("ffmpeg is installed on this machine, so .opus is playable here.");
            }

            await dialog.HandleKeyEventAsync(Key('\r', ConsoleKey.Enter));
            dialog.OnInitializeLayout();

            Assert.AreEqual(0, player.Playlist.Count, "an unplayable file must not be queued");
            StringAssert.Contains(Render(dialog), "ffmpeg", "the viewer says why it cannot play the file");
        }
    }

    [TestMethod]
    public async Task Show_AMissingFile_ReportsTheErrorRatherThanThrowing()
    {
        var (dialog, player, _) = CreateDialog(Path.Combine(_tempRoot, "gone.mp3"));
        using (player)
        {
            await ShowAsync(dialog);
            dialog.OnInitializeLayout();

            Assert.AreNotEqual(string.Empty, dialog.Metadata.Error);
            StringAssert.Contains(Render(dialog), "no longer exists");
        }
    }

    [TestMethod]
    public async Task S_InAViewerOfAnotherFile_DoesNotStopWhatIsPlaying()
    {
        // The player is shared with the music panels; 's' in a viewer opened over some other file
        // used to stop whatever they were playing.
        var playing = TestAudioFiles.WriteSilence(_tempRoot, "playing.wav", TimeSpan.FromSeconds(3), sampleRate: 8000);
        var other = TestAudioFiles.WriteSilence(_tempRoot, "other.wav", TimeSpan.FromSeconds(3), sampleRate: 8000);
        var (dialog, player, devices) = CreateDialog(playing);
        devices.WriteDelay = PlaybackPacing;
        player.RepeatMode = RepeatMode.One;
        using (player)
        {
            await ShowAsync(dialog);
            await dialog.HandleKeyEventAsync(Key(' ', ConsoleKey.Spacebar));
            await WaitForAsync(() => player.State == PlaybackState.Playing, "playback to start");

            var otherViewer = new MusicFileViewerDialog(player, devices, new MusicMetadataService(), Mock.Of<iConsole.Services.IBeepService>(), Mock.Of<iConsole.Services.IDateTimeProviderService>(), CreateThemeService())
            {
                FilePath = other,
            };
            await ShowAsync(otherViewer);
            await otherViewer.HandleKeyEventAsync(Key('s', ConsoleKey.S));

            Assert.AreEqual(PlaybackState.Playing, player.State);
            Assert.AreEqual("This file is not playing.", otherViewer.StatusForTest);
        }
    }

    [TestMethod]
    public async Task Seeking_AFileThatIsNotPlaying_SaysHowToStartIt()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "idle.wav", TimeSpan.FromSeconds(1), sampleRate: 8000);
        var (dialog, player, _) = CreateDialog(path);
        using (player)
        {
            await ShowAsync(dialog);

            await dialog.HandleKeyEventAsync(Key('\0', ConsoleKey.RightArrow));

            StringAssert.Contains(dialog.StatusForTest, "Space plays it");
        }
    }

    [TestMethod]
    public async Task TheFooterNamesTheLongSeek()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "footer.wav", TimeSpan.FromSeconds(1), sampleRate: 8000);
        var (dialog, player, _) = CreateDialog(path);
        using (player)
        {
            await ShowAsync(dialog);

            StringAssert.Contains(Render(dialog), "Shift+←/→:seek further");
        }
    }
}
