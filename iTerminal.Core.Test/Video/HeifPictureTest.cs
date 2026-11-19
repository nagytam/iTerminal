using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

/// <summary>
/// A HEIF picture is a HEVC still in an ISO container. The picture viewer hands it to ffmpeg, which reads the container
/// and decodes the picture, so this test decodes the committed sample through the real decoder.
/// </summary>
[TestClass]
public class HeifPictureTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("example.heic")]
    [DataRow("example.heif")]
    public void TheHeifSamples_DecodeThroughFfmpeg(string sample)
    {
        var factory = new FfmpegVideoDecoderFactory();
        if (!factory.IsAvailable)
        {
            Assert.Inconclusive("needs ffmpeg and ffprobe on PATH");
        }

        string path = Path.Combine(AppContext.BaseDirectory, "FileTypes", sample);
        using IVideoDecoder decoder = factory.Create(path);
        decoder.Open(TimeSpan.Zero, 16, 12);
        byte[] rgb = new byte[16 * 12 * 3];

        Assert.IsTrue(decoder.ReadFrame(rgb), "ffmpeg decodes the HEIC picture");
    }
}
