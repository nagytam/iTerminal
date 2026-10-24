using iTerminal.Core.Audio;
using iTerminal.Core.Audio.Decoders;

namespace iTerminal.Core.Test.Audio;

[TestClass]
[DoNotParallelize]
public class AudioDecoderFactoryTest
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DecoderFactoryTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        FfmpegAudioDecoder.SetAvailabilityForTest(null);
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
        }
    }

    [TestMethod]
    public void IsAudioFile_AcceptsBothNativeAndFfmpegFormats()
    {
        Assert.IsTrue(AudioDecoderFactory.IsAudioFile("song.mp3"));
        Assert.IsTrue(AudioDecoderFactory.IsAudioFile("song.WAV"));
        Assert.IsTrue(AudioDecoderFactory.IsAudioFile("song.flac"));
        Assert.IsTrue(AudioDecoderFactory.IsAudioFile("song.opus"));
        Assert.IsFalse(AudioDecoderFactory.IsAudioFile("song.txt"));
        Assert.IsFalse(AudioDecoderFactory.IsAudioFile(string.Empty));
    }

    [TestMethod]
    public void IsSupported_NativeFormats_DoNotDependOnFfmpeg()
    {
        FfmpegAudioDecoder.SetAvailabilityForTest(false);

        Assert.IsTrue(AudioDecoderFactory.IsSupported("song.mp3"));
        Assert.IsTrue(AudioDecoderFactory.IsSupported("song.wav"));
    }

    [TestMethod]
    public void IsSupported_FfmpegFormats_TrackFfmpegAvailability()
    {
        FfmpegAudioDecoder.SetAvailabilityForTest(false);
        Assert.IsFalse(AudioDecoderFactory.IsSupported("song.flac"));

        FfmpegAudioDecoder.SetAvailabilityForTest(true);
        Assert.IsTrue(AudioDecoderFactory.IsSupported("song.flac"));
    }

    [TestMethod]
    public void Create_ForAWaveFile_ReturnsTheManagedWaveDecoder()
    {
        var path = TestAudioFiles.WriteWave(_tempRoot, "a.wav", TestAudioFiles.Pcm16(1, 2, 3, 4));

        using var decoder = AudioDecoderFactory.Create(path);

        Assert.IsInstanceOfType<WaveAudioDecoder>(decoder);
    }

    [TestMethod]
    public void Create_ForAMissingFile_ThrowsFileNotFound()
    {
        Assert.ThrowsExactly<FileNotFoundException>(
            () => AudioDecoderFactory.Create(Path.Combine(_tempRoot, "nope.wav")));
    }

    [TestMethod]
    public void Create_ForAnFfmpegFormatWithoutFfmpeg_ExplainsWhatToInstall()
    {
        FfmpegAudioDecoder.SetAvailabilityForTest(false);
        var path = Path.Combine(_tempRoot, "song.flac");
        File.WriteAllBytes(path, new byte[128]);

        var exception = Assert.ThrowsExactly<NotSupportedException>(() => AudioDecoderFactory.Create(path));

        StringAssert.Contains(exception.Message, "ffmpeg");
    }

    [TestMethod]
    public void Create_ForACompressedWaveWithoutFfmpeg_ReportsItRatherThanReturningGarbage()
    {
        FfmpegAudioDecoder.SetAvailabilityForTest(false);
        var path = TestAudioFiles.WriteWave(
            _tempRoot, "adpcm.wav", new byte[64], sampleRate: 8000, channels: 1, bitsPerSample: 4, formatTag: 0x0011);

        Assert.ThrowsExactly<NotSupportedException>(() => AudioDecoderFactory.Create(path));
    }

    [TestMethod]
    public void AllExtensions_IsTheUnionOfBothTiers()
    {
        foreach (var extension in AudioDecoderFactory.NativeExtensions.Concat(AudioDecoderFactory.FfmpegExtensions))
        {
            Assert.IsTrue(AudioDecoderFactory.AllExtensions.Contains(extension), $"{extension} is missing");
        }
    }
}
