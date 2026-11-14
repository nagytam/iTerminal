using iTerminal.Core.Video;
using SkiaSharp;

namespace iTerminal.Core.Test.Video;

[TestClass]
public class RawPreviewVideoDecoderTest
{
    /// <summary>A solid JPEG of the given size: red, so a decoded pixel is easy to recognise.</summary>
    private static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        bitmap.Erase(new SKColor(255, 0, 0));
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        return data.ToArray();
    }

    /// <summary>Bytes that look like the start of a JPEG but never end as one: what a false match in sensor data looks like.</summary>
    private static readonly byte[] FalseStart = [0xFF, 0xD8, 0xFF, 0x00, 0x11, 0x22];

    [TestMethod]
    public void FindJpegs_ListsTheLargestFirst()
    {
        byte[] thumbnail = Jpeg(8, 8);
        byte[] preview = Jpeg(40, 20);
        byte[] file = [.. new byte[16], .. thumbnail, .. new byte[8], .. preview, .. new byte[4]];

        var found = RawPreviewVideoDecoder.FindJpegs(file);

        Assert.HasCount(2, found);
        Assert.AreEqual(preview.Length, found[0].Length);
        Assert.AreEqual(16 + thumbnail.Length + 8, found[0].Start);
        Assert.AreEqual(thumbnail.Length, found[1].Length);
    }

    [TestMethod]
    public void FalseStarts_WithoutAnEndAreNotCandidates()
    {
        byte[] file = [.. FalseStart, .. new byte[10]];

        Assert.IsEmpty(RawPreviewVideoDecoder.FindJpegs(file));
    }

    [TestMethod]
    public void HasJpegHeader_AcceptsARealJpegAndRejectsAFalseMatch()
    {
        Assert.IsTrue(RawPreviewVideoDecoder.HasJpegHeader(Jpeg(8, 8)));
        Assert.IsFalse(RawPreviewVideoDecoder.HasJpegHeader(FalseStart));
        Assert.IsFalse(RawPreviewVideoDecoder.HasJpegHeader([0xFF, 0xD8, 0xFF, 0xD9]), "an end marker before the scan");
        Assert.IsFalse(RawPreviewVideoDecoder.HasJpegHeader([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00]), "a segment length below 2");
    }

    [TestMethod]
    public void Decoder_ShowsTheEmbeddedPreviewAtItsOwnSize()
    {
        byte[] preview = Jpeg(40, 20);
        byte[] file = [.. FalseStart, .. new byte[32], .. Jpeg(8, 8), .. preview, .. new byte[64]];
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cr2");
        try
        {
            File.WriteAllBytes(path, file);
            using var decoder = new RawPreviewVideoDecoder(path);
            decoder.Open(TimeSpan.Zero, 40, 20);
            var rgb = new byte[40 * 20 * 3];

            Assert.AreEqual(40, decoder.Info.Width, "the largest preview is the one shown");
            Assert.AreEqual(20, decoder.Info.Height);
            Assert.IsTrue(decoder.ReadFrame(rgb));
            Assert.IsTrue(rgb[0] > 200 && rgb[1] < 60 && rgb[2] < 60, "a red picture");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void AFileWithNoPreview_SaysSo()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.nef");
        try
        {
            File.WriteAllBytes(path, [.. FalseStart, .. new byte[100]]);

            InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(() => new RawPreviewVideoDecoder(path));
            StringAssert.Contains(exception.Message, "JPEG preview");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void IsRawFile_RecognisesTheCameraExtensionsInAnyCase()
    {
        Assert.IsTrue(RawPreviewVideoDecoder.IsRawFile("shot.CR2"));
        Assert.IsTrue(RawPreviewVideoDecoder.IsRawFile("shot.dng"));
        Assert.IsTrue(RawPreviewVideoDecoder.IsRawFile("shot.Nef"));
        Assert.IsFalse(RawPreviewVideoDecoder.IsRawFile("photo.jpg"));
    }
}
