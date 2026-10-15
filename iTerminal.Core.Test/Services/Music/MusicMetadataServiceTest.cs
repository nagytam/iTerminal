using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// What the F3 music viewer is handed. The point of these tests is that a file always yields
/// something showable — tags without a duration, or a duration without tags — and never an
/// exception.
/// </summary>
[TestClass]
public class MusicMetadataServiceTest
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "MusicMetaTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    [TestMethod]
    public async Task ReadAsync_Wave_ReportsDurationAndFormat()
    {
        var path = TestAudioFiles.WriteSilence(_tempRoot, "silence.wav", TimeSpan.FromSeconds(2), sampleRate: 8000);

        MusicFileMetadata metadata = await new MusicMetadataService().ReadAsync(path);

        Assert.AreEqual("WAV", metadata.Format);
        Assert.AreEqual(8000, metadata.SampleRate);
        Assert.AreEqual(2, metadata.Channels);
        Assert.AreEqual(2, (int)Math.Round(metadata.Duration.TotalSeconds));
        Assert.IsTrue(metadata.CanPlay, "WAVE needs no external tooling");
        Assert.IsTrue(metadata.Size > 0);
        Assert.AreEqual(string.Empty, metadata.Error);
        StringAssert.Contains(metadata.FormatSummary, "8 kHz");
        StringAssert.Contains(metadata.FormatSummary, "stereo");
    }

    [TestMethod]
    public async Task ReadAsync_TaggedMp3_ReturnsTagsEvenWhenTheAudioCannotBeDecoded()
    {
        // The payload is not real MPEG audio, so the decoder fails — the tags must survive that.
        var path = TestAudioFiles.WriteId3V2File(_tempRoot, "song.mp3", "Comfortably Numb", "Pink Floyd", "The Wall");

        MusicFileMetadata metadata = await new MusicMetadataService().ReadAsync(path);

        Assert.AreEqual("Pink Floyd", metadata.Artist);
        Assert.AreEqual("Comfortably Numb", metadata.Title);
        Assert.AreEqual("The Wall", metadata.Album);
        Assert.AreEqual("ID3v2.3", metadata.TagSet.Source);
        Assert.AreEqual(string.Empty, metadata.Error);
    }

    [TestMethod]
    public async Task ReadAsync_Flac_UsesTheMetadataBlocksRatherThanADecoder()
    {
        var path = TestAudioFiles.WriteFlac(
            _tempRoot,
            "lossless.flac",
            sampleRate: 44100,
            channels: 2,
            bitsPerSample: 16,
            totalSamples: 88200,
            tags: [("ARTIST", "Boards of Canada"), ("TITLE", "Roygbiv")]);

        MusicFileMetadata metadata = await new MusicMetadataService().ReadAsync(path);

        Assert.AreEqual("FLAC", metadata.Format);
        Assert.AreEqual("Boards of Canada", metadata.Artist);
        Assert.AreEqual("Roygbiv", metadata.Title);
        Assert.AreEqual(TimeSpan.FromSeconds(2), metadata.Duration);
        Assert.AreEqual(16, metadata.BitsPerSample);
        StringAssert.Contains(metadata.FormatSummary, "16 bit");
    }

    [TestMethod]
    public async Task ReadAsync_UntaggedFile_FallsBackToTheFileNameForTheTitle()
    {
        var path = Path.Combine(_tempRoot, "Unknown Track.mp3");
        File.WriteAllBytes(path, new byte[256]);

        MusicFileMetadata metadata = await new MusicMetadataService().ReadAsync(path);

        Assert.AreEqual("Unknown Track", metadata.Title);
        Assert.AreEqual(string.Empty, metadata.Artist);
        Assert.IsTrue(metadata.TagSet.IsEmpty);
    }

    [TestMethod]
    public async Task ReadAsync_MissingFile_ReportsTheErrorInsteadOfThrowing()
    {
        MusicFileMetadata metadata = await new MusicMetadataService().ReadAsync(Path.Combine(_tempRoot, "gone.mp3"));

        Assert.AreNotEqual(string.Empty, metadata.Error);
        Assert.AreEqual(TimeSpan.Zero, metadata.Duration);
    }

    [TestMethod]
    public void IsMusicFile_FollowsTheDecoderFactory()
    {
        var service = new MusicMetadataService();

        Assert.IsTrue(service.IsMusicFile(@"C:\Music\a.mp3"));
        Assert.IsTrue(service.IsMusicFile(@"C:\Music\a.flac"));
        Assert.IsFalse(service.IsMusicFile(@"C:\Music\readme.txt"));
    }

    [TestMethod]
    public async Task ReadAsync_ProducesATrackTheParticularPlayerCanQueue()
    {
        var path = TestAudioFiles.WriteId3V2File(_tempRoot, "queue.mp3", "Time", "Pink Floyd", "Dark Side");

        MusicTrack track = (await new MusicMetadataService().ReadAsync(path)).ToTrack();

        Assert.AreEqual(path, track.Path);
        Assert.AreEqual("Pink Floyd - Time", track.DisplayName);
    }
}
