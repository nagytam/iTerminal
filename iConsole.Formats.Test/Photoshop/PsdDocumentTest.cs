using System.Buffers.Binary;
using iConsole.Photoshop;
using iConsole.Services;
using iConsole.Test.Common;
using iConsole.Test.Services;
using SkiaSharp;

namespace iConsole.Test.Photoshop;

[TestClass]
public class PsdDocumentTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Parse_TheExampleFileReadsItsHeaderLayersAndMergedPicture()
    {
        PsdDocument document = PsdDocument.Parse(SampleFiles.Bytes("example.psd"));

        Assert.AreEqual(1, document.Version);
        Assert.AreEqual(6, document.Width);
        Assert.AreEqual(4, document.Height);
        Assert.AreEqual(3, document.Channels);
        Assert.AreEqual(8, document.Depth);
        Assert.AreEqual("RGB", document.ColorMode);
        Assert.AreEqual(1, document.LayerCount);
        Assert.AreEqual("RLE", document.Compression);
        Assert.AreEqual(PsdPictureStatus.Drawn, document.PictureStatus);
        Assert.IsNotNull(document.Picture);
        Assert.AreEqual(new SKColor(230, 180, 230), document.Picture.GetPixel(0, 0), "even cells keep the first colour");
        Assert.AreEqual(new SKColor(25, 75, 25), document.Picture.GetPixel(1, 0), "odd cells are the inverse");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_ARawMergedPictureIsDrawn()
    {
        // 2 x 1 RGB, raw: the red plane, the green plane, then the blue plane.
        byte[] data = [200, 10, 0, 0, 0, 255];
        PsdDocument document = PsdDocument.Parse(BuildPsd(width: 2, height: 1, channels: 3, depth: 8, mode: 3, compression: 0, data));

        Assert.AreEqual(PsdPictureStatus.Drawn, document.PictureStatus);
        Assert.AreEqual(new SKColor(200, 0, 0), document.Picture!.GetPixel(0, 0));
        Assert.AreEqual(new SKColor(10, 0, 255), document.Picture.GetPixel(1, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_AZipCompressedPictureIsReportedNotDrawn()
    {
        PsdDocument document = PsdDocument.Parse(BuildPsd(width: 1, height: 1, channels: 3, depth: 8, mode: 3, compression: 2, [1, 2, 3]));

        Assert.AreEqual(PsdPictureStatus.Compressed, document.PictureStatus);
        Assert.IsNull(document.Picture);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_ACmykPictureIsReportedNotDrawn()
    {
        PsdDocument document = PsdDocument.Parse(BuildPsd(width: 1, height: 1, channels: 4, depth: 8, mode: 4, compression: 0, [1, 2, 3, 4]));

        Assert.AreEqual("CMYK", document.ColorMode);
        Assert.AreEqual(PsdPictureStatus.NotDrawn, document.PictureStatus);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Parse_ANonPhotoshopFileIsRefused()
    {
        Assert.ThrowsExactly<FormatException>(() => PsdDocument.Parse(System.Text.Encoding.ASCII.GetBytes("this is not a psd file at all")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheViewerShowsTheHeaderAndThePicture()
    {
        var textModel = new iConsole.Model.TextModel();

        await new FileToTextConverterService().ReadFileAsync(
            new iConsole.Data.Url(SampleFiles.Path("example.psd")), new LocalFileSystemAdapter(), textModel);

        List<string> lines = textModel.Lines.ToList();
        Assert.IsTrue(lines.Any(l => l.Contains("6 × 4 pixels")), "the size is shown");
        Assert.IsTrue(lines.Any(l => l.Contains("RGB")), "the colour mode is shown");
        Assert.IsTrue(lines.Any(l => l.Any(c => c is '█' or '▓' or '▒' or '░' or ' ')), "the merged picture is drawn as text art");
    }

    /// <summary>A PSD file with an empty layer section and the given merged picture data.</summary>
    private static byte[] BuildPsd(int width, int height, int channels, int depth, int mode, int compression, byte[] data)
    {
        var bytes = new List<byte>();
        bytes.AddRange("8BPS"u8.ToArray());
        bytes.AddRange(BigEndian((ushort)1));
        bytes.AddRange(new byte[6]);
        bytes.AddRange(BigEndian((ushort)channels));
        bytes.AddRange(BigEndian((uint)height));
        bytes.AddRange(BigEndian((uint)width));
        bytes.AddRange(BigEndian((ushort)depth));
        bytes.AddRange(BigEndian((ushort)mode));
        bytes.AddRange(BigEndian(0u)); // colour mode data
        bytes.AddRange(BigEndian(0u)); // image resources
        bytes.AddRange(BigEndian(0u)); // layer and mask information
        bytes.AddRange(BigEndian((ushort)compression));
        bytes.AddRange(data);
        return [.. bytes];
    }

    private static byte[] BigEndian(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] BigEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }
}
