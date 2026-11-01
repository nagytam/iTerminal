using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;

namespace iTerminal.Core.Test.Services.Music;

[TestClass]
public class PlaylistServiceTest
{
    private string _tempRoot = string.Empty;
    private PlaylistService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "PlaylistServiceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new PlaylistService(Path.Combine(_tempRoot, "state"));
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

    [Timeout(60_000)]
    [TestMethod]
    public void SaveThenLoad_RoundTripsPathsAndMetadata()
    {
        var tracks = new[]
        {
            new MusicTrack(Path.Combine(_tempRoot, "one.mp3"))
            {
                Title = "One", Artist = "Artist A", Duration = TimeSpan.FromSeconds(185),
            },
            new MusicTrack(Path.Combine(_tempRoot, "two.mp3")) { Title = "Two" },
        };

        var playlistPath = Path.Combine(_tempRoot, "list.m3u8");
        _service.Save(playlistPath, tracks);
        var loaded = _service.Load(playlistPath);

        Assert.AreEqual(2, loaded.Count);
        Assert.AreEqual(tracks[0].Path, loaded[0].Path);
        Assert.AreEqual("One", loaded[0].Title);
        Assert.AreEqual("Artist A", loaded[0].Artist);
        Assert.AreEqual(185, loaded[0].Duration.TotalSeconds, 0.5);
        Assert.AreEqual("Two", loaded[1].Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Save_WritesExtendedM3UHeader()
    {
        var playlistPath = Path.Combine(_tempRoot, "list.m3u8");
        _service.Save(playlistPath, [new MusicTrack(Path.Combine(_tempRoot, "a.mp3")) { Title = "A" }]);

        var lines = File.ReadAllLines(playlistPath);

        Assert.AreEqual("#EXTM3U", lines[0]);
        Assert.IsTrue(lines[1].StartsWith("#EXTINF:", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Load_ResolvesRelativeEntriesAgainstThePlaylistFolder()
    {
        var musicFolder = Path.Combine(_tempRoot, "album");
        Directory.CreateDirectory(musicFolder);
        var playlistPath = Path.Combine(musicFolder, "list.m3u");
        File.WriteAllText(playlistPath, "#EXTM3U\r\nsong.mp3\r\n");

        var loaded = _service.Load(playlistPath);

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(Path.Combine(musicFolder, "song.mp3"), loaded[0].Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Load_PlainListWithoutDirectives_IsAccepted()
    {
        var playlistPath = Path.Combine(_tempRoot, "plain.m3u");
        File.WriteAllText(playlistPath, $"{Path.Combine(_tempRoot, "a.mp3")}\n{Path.Combine(_tempRoot, "b.mp3")}\n");

        var loaded = _service.Load(playlistPath);

        Assert.AreEqual(2, loaded.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Load_SkipsRemoteEntriesTheLocalDecoderCannotOpen()
    {
        var playlistPath = Path.Combine(_tempRoot, "remote.m3u");
        File.WriteAllText(playlistPath, $"http://example.com/stream.mp3\n{Path.Combine(_tempRoot, "a.mp3")}\n");

        var loaded = _service.Load(playlistPath);

        Assert.AreEqual(1, loaded.Count);
        Assert.IsFalse(loaded[0].Path.Contains("http", StringComparison.OrdinalIgnoreCase));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Load_MissingFile_ReturnsEmptyRatherThanThrowing()
    {
        var loaded = _service.Load(Path.Combine(_tempRoot, "nope.m3u"));

        Assert.AreEqual(0, loaded.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseExtInf_SplitsDurationArtistAndTitle()
    {
        var (duration, artist, title) = PlaylistService.ParseExtInf("#EXTINF:213,Pink Floyd - Time");

        Assert.AreEqual(213, duration.TotalSeconds, 0.001);
        Assert.AreEqual("Pink Floyd", artist);
        Assert.AreEqual("Time", title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseExtInf_WithoutArtistSeparator_TreatsWholeLabelAsTitle()
    {
        var (duration, artist, title) = PlaylistService.ParseExtInf("#EXTINF:-1,Just A Title");

        Assert.AreEqual(TimeSpan.Zero, duration, "A -1 duration means unknown");
        Assert.AreEqual(string.Empty, artist);
        Assert.AreEqual("Just A Title", title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateTracks_ForASingleAudioFile_ReturnsOneTrack()
    {
        var path = TestAudioFiles.WriteWave(_tempRoot, "song.wav", TestAudioFiles.Pcm16(1, 2, 3, 4));

        var tracks = _service.CreateTracks(path);

        Assert.AreEqual(1, tracks.Count);
        Assert.AreEqual(path, tracks[0].Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateTracks_ForANonAudioFile_ReturnsNothing()
    {
        var path = Path.Combine(_tempRoot, "notes.txt");
        File.WriteAllText(path, "hello");

        Assert.AreEqual(0, _service.CreateTracks(path).Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateTracks_ForAFolder_CollectsAudioFilesRecursively()
    {
        var nested = Path.Combine(_tempRoot, "album", "disc1");
        Directory.CreateDirectory(nested);
        TestAudioFiles.WriteWave(Path.Combine(_tempRoot, "album"), "a.wav", TestAudioFiles.Pcm16(1, 2));
        TestAudioFiles.WriteWave(nested, "b.wav", TestAudioFiles.Pcm16(3, 4));
        File.WriteAllText(Path.Combine(nested, "cover.jpg"), "not audio");

        var tracks = _service.CreateTracks(Path.Combine(_tempRoot, "album"));

        Assert.AreEqual(2, tracks.Count);
        CollectionAssert.AllItemsAreUnique(tracks.Select(t => t.Path).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateTracks_ForAFolder_WithoutRecursion_StaysAtTheTopLevel()
    {
        var nested = Path.Combine(_tempRoot, "album", "disc1");
        Directory.CreateDirectory(nested);
        TestAudioFiles.WriteWave(Path.Combine(_tempRoot, "album"), "a.wav", TestAudioFiles.Pcm16(1, 2));
        TestAudioFiles.WriteWave(nested, "b.wav", TestAudioFiles.Pcm16(3, 4));

        var tracks = _service.CreateTracks(Path.Combine(_tempRoot, "album"), recursive: false);

        Assert.AreEqual(1, tracks.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateTracks_ForAPlaylistFile_ExpandsItsEntries()
    {
        var playlistPath = Path.Combine(_tempRoot, "list.m3u8");
        _service.Save(playlistPath, [new MusicTrack(Path.Combine(_tempRoot, "a.mp3"))]);

        var tracks = _service.CreateTracks(playlistPath);

        Assert.AreEqual(1, tracks.Count);
    }

    /// <summary>
    /// The queue lives in the database now, but the file an older version left behind still has to
    /// be readable — that one read is what carries an upgrading user's queue over.
    /// </summary>
    [Timeout(60_000)]
    [TestMethod]
    public void LoadLast_ReadsTheQueueFileWrittenByOlderVersions()
    {
        var tracks = new[]
        {
            new MusicTrack(Path.Combine(_tempRoot, "a.mp3")) { Title = "A" },
            new MusicTrack(Path.Combine(_tempRoot, "b.mp3")) { Title = "B" },
        };

        _service.Save(_service.LastPlaylistPath, tracks);
        var restored = _service.LoadLast();

        Assert.AreEqual(2, restored.Count);
        Assert.AreEqual("A", restored[0].Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LoadLast_WithNothingSaved_ReturnsEmpty()
    {
        Assert.AreEqual(0, _service.LoadLast().Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsPlaylistFile_RecognisesM3UAndM3U8Only()
    {
        Assert.IsTrue(_service.IsPlaylistFile("a.m3u"));
        Assert.IsTrue(_service.IsPlaylistFile("a.M3U8"));
        Assert.IsFalse(_service.IsPlaylistFile("a.mp3"));
        Assert.IsFalse(_service.IsPlaylistFile(string.Empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Save_CreatesMissingDirectories()
    {
        var nestedPath = Path.Combine(_tempRoot, "new", "deeper", "list.m3u8");

        _service.Save(nestedPath, [new MusicTrack("a.mp3")]);

        Assert.IsTrue(File.Exists(nestedPath));
    }
}
