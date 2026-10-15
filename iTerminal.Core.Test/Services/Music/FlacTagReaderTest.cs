using iTerminal.Core.Services.Music;
using iTerminal.Core.Test.Audio;

namespace iTerminal.Core.Test.Services.Music;

/// <summary>
/// FLAC is played through ffmpeg, so its metadata blocks are the only source of tags and playing
/// time the app has without forking a process — which is what these tests pin down.
/// </summary>
[TestClass]
public class FlacTagReaderTest
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "FlacTagTest_" + Guid.NewGuid().ToString("N")[..8]);
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
    public void Read_ReturnsStreamInfoAndVorbisComments()
    {
        var path = TestAudioFiles.WriteFlac(
            _tempRoot,
            "track.flac",
            sampleRate: 48000,
            channels: 2,
            bitsPerSample: 24,
            totalSamples: 96000,
            tags:
            [
                ("TITLE", "Teardrop"),
                ("ARTIST", "Massive Attack"),
                ("ALBUM", "Mezzanine"),
                ("TRACKNUMBER", "5"),
                ("DATE", "1998"),
            ]);

        FlacTagReader.FlacInfo info = FlacTagReader.Read(path);

        Assert.AreEqual(48000, info.SampleRate);
        Assert.AreEqual(2, info.Channels);
        Assert.AreEqual(24, info.BitsPerSample);
        Assert.AreEqual(TimeSpan.FromSeconds(2), info.Duration);

        Assert.AreEqual("Vorbis comment", info.Tags.Source);
        Assert.AreEqual("Teardrop", info.Tags.Title);
        Assert.AreEqual("Massive Attack", info.Tags.Artist);
        Assert.AreEqual("Mezzanine", info.Tags.Album);
        Assert.AreEqual("5", info.Tags.Tags.Single(tag => tag.Name == "Track").Value);
        Assert.AreEqual("1998", info.Tags.Tags.Single(tag => tag.Name == "Date").Value);
    }

    [TestMethod]
    public void Read_KeepsAnUnknownFieldUnderItsOwnName()
    {
        var path = TestAudioFiles.WriteFlac(_tempRoot, "odd.flac", tags: [("REPLAYGAIN_TRACK_GAIN", "-3.21 dB")]);

        FlacTagReader.FlacInfo info = FlacTagReader.Read(path);

        Assert.AreEqual("REPLAYGAIN_TRACK_GAIN", info.Tags.Tags.Single().Name);
        Assert.AreEqual("-3.21 dB", info.Tags.Tags.Single().Value);
    }

    [TestMethod]
    public void Read_WithoutTags_StillReportsTheAudioFormat()
    {
        var path = TestAudioFiles.WriteFlac(_tempRoot, "bare.flac", sampleRate: 44100, channels: 1, bitsPerSample: 16, totalSamples: 44100);

        FlacTagReader.FlacInfo info = FlacTagReader.Read(path);

        Assert.IsTrue(info.Tags.IsEmpty);
        Assert.AreEqual(44100, info.SampleRate);
        Assert.AreEqual(1, info.Channels);
        Assert.AreEqual(TimeSpan.FromSeconds(1), info.Duration);
    }

    [TestMethod]
    public void Read_NonFlacOrMissingFile_ReturnsEmptyRatherThanThrowing()
    {
        var notFlac = Path.Combine(_tempRoot, "fake.flac");
        File.WriteAllBytes(notFlac, new byte[256]);

        Assert.AreEqual(FlacTagReader.FlacInfo.Empty, FlacTagReader.Read(notFlac));
        Assert.AreEqual(FlacTagReader.FlacInfo.Empty, FlacTagReader.Read(Path.Combine(_tempRoot, "nope.flac")));
    }

    [TestMethod]
    public void CanRead_OnlyClaimsFlacFiles()
    {
        Assert.IsTrue(FlacTagReader.CanRead(@"C:\Music\a.FLAC"));
        Assert.IsFalse(FlacTagReader.CanRead(@"C:\Music\a.mp3"));
        Assert.IsFalse(FlacTagReader.CanRead(string.Empty));
    }
}
