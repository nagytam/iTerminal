using iTerminal.Core.Audio;
using iTerminal.Core.Audio.Decoders;

namespace iTerminal.Core.Test.Audio;

/// <summary>
/// The audio formats ffmpeg reads: each extension is recognised, and each sample in TestFiles (one short tone per format
/// that this ffmpeg can write) decodes through ffmpeg. The formats with no sample are recognised by extension only.
/// </summary>
[TestClass]
public class FfmpegAudioFormatsTest
{
    private static readonly string SampleDirectory = Path.Combine(AppContext.BaseDirectory, "Audio", "TestFiles");

    [Timeout(60_000)]
    [TestMethod]
    public void EveryFfmpegAudioExtension_IsRecognised()
    {
        foreach (string extension in AudioDecoderFactory.FfmpegExtensions)
        {
            Assert.IsTrue(AudioDecoderFactory.IsAudioFile("track" + extension), extension);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DemuxOnlyFormats_AreRecognised()
    {
        // Formats with no sample: ffmpeg reads them, but this test does not write one.
        string[] extensions = [".dts", ".tta", ".thd", ".dsf", ".dff", ".mod", ".s3m", ".xm", ".it", ".brstm", ".tak", ".vqf", ".qoa"];

        foreach (string extension in extensions)
        {
            Assert.IsTrue(AudioDecoderFactory.IsAudioFile("track" + extension), extension);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SampleFiles_AreAllAudioByExtension()
    {
        string[] samples = Directory.GetFiles(SampleDirectory, "sample.*");

        Assert.IsTrue(samples.Length >= 17, $"expected the sample files next to the test, found {samples.Length}");
        foreach (string sample in samples)
        {
            Assert.IsTrue(AudioDecoderFactory.IsAudioFile(sample), sample);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SampleFiles_DecodeThroughFfmpeg()
    {
        if (!FfmpegAudioDecoder.IsAvailable())
        {
            Assert.Inconclusive("ffmpeg is not on the PATH");
        }

        foreach (string sample in Directory.GetFiles(SampleDirectory, "sample.*"))
        {
            using FfmpegAudioDecoder decoder = new(sample);
            byte[] buffer = new byte[4096];

            int read = decoder.Read(buffer);

            Assert.IsTrue(read > 0, $"{Path.GetFileName(sample)} gave no audio");
        }
    }
}
