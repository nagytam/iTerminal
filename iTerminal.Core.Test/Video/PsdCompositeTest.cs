using iTerminal.Core.Video;
using SkiaSharp;

namespace iTerminal.Core.Test.Video;

[TestClass]
public class PsdCompositeTest
{
    private static void Be16(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void Be32(List<byte> bytes, long value)
    {
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            bytes.Add((byte)(value >> shift));
        }
    }

    /// <summary>A PackBits row of literal bytes only: one control byte, then the bytes.</summary>
    private static byte[] Literal(byte[] row) => [(byte)(row.Length - 1), .. row];

    /// <summary>
    /// A Photoshop file with the given header and composite image data. The colour mode data is empty unless given, and the
    /// image resources and the layer block are empty.
    /// </summary>
    private static byte[] Psd(int channels, int width, int height, int depth, int mode, byte[] imageData, byte[]? colorModeData = null, int version = 1)
    {
        var bytes = new List<byte>("8BPS"u8.ToArray());
        Be16(bytes, version);
        bytes.AddRange(new byte[6]);
        Be16(bytes, channels);
        Be32(bytes, height);
        Be32(bytes, width);
        Be16(bytes, depth);
        Be16(bytes, mode);
        byte[] colorMode = colorModeData ?? [];
        Be32(bytes, colorMode.Length);
        bytes.AddRange(colorMode);
        Be32(bytes, 0);
        if (version == 2)
        {
            Be32(bytes, 0);
            Be32(bytes, 0);
        }
        else
        {
            Be32(bytes, 0);
        }

        bytes.AddRange(imageData);
        return bytes.ToArray();
    }

    private static byte[] Raw(params byte[][] planes)
    {
        var data = new List<byte>();
        Be16(data, 0);
        foreach (byte[] plane in planes)
        {
            data.AddRange(plane);
        }

        return data.ToArray();
    }

    private static byte[] RunLength(params byte[][] rows)
    {
        var data = new List<byte>();
        Be16(data, 1);
        foreach (byte[] row in rows)
        {
            Be16(data, Literal(row).Length);
        }

        foreach (byte[] row in rows)
        {
            data.AddRange(Literal(row));
        }

        return data.ToArray();
    }

    private static SKBitmap Decode(byte[] file)
    {
        using var stream = new MemoryStream(file);
        PsdHeader header = PsdComposite.ReadHeader(stream);
        return PsdComposite.Decode(stream, header);
    }

    [TestMethod]
    public void RawRgb_IsReadPixelForPixel()
    {
        byte[] red = [255, 0, 0, 255];
        byte[] green = [0, 255, 0, 255];
        byte[] blue = [0, 0, 255, 255];

        using SKBitmap bitmap = Decode(Psd(3, 2, 2, 8, 3, Raw(red, green, blue)));

        Assert.AreEqual(2, bitmap.Width);
        Assert.AreEqual(2, bitmap.Height);
        Assert.AreEqual(new SKColor(255, 0, 0), bitmap.GetPixel(0, 0));
        Assert.AreEqual(new SKColor(0, 255, 0), bitmap.GetPixel(1, 0));
        Assert.AreEqual(new SKColor(0, 0, 255), bitmap.GetPixel(0, 1));
        Assert.AreEqual(new SKColor(255, 255, 255), bitmap.GetPixel(1, 1));
    }

    [TestMethod]
    public void RunLength_IsUnpackedRowByRow()
    {
        byte[] red = [255, 0, 0, 255];
        byte[] green = [0, 255, 0, 255];
        byte[] blue = [0, 0, 255, 255];
        // Each channel is two rows: row 0 is the first two pixels, row 1 the last two.
        byte[] data = RunLength(
            [red[0], red[1]], [red[2], red[3]],
            [green[0], green[1]], [green[2], green[3]],
            [blue[0], blue[1]], [blue[2], blue[3]]);

        using SKBitmap bitmap = Decode(Psd(3, 2, 2, 8, 3, data));

        Assert.AreEqual(new SKColor(255, 0, 0), bitmap.GetPixel(0, 0));
        Assert.AreEqual(new SKColor(0, 255, 0), bitmap.GetPixel(1, 0));
        Assert.AreEqual(new SKColor(0, 0, 255), bitmap.GetPixel(0, 1));
        Assert.AreEqual(new SKColor(255, 255, 255), bitmap.GetPixel(1, 1));
    }

    [TestMethod]
    public void PackBits_RepeatsARunAndCopiesLiterals()
    {
        byte[] destination = new byte[4];

        // 0xFE is -2: three copies of 10. 0x00 is one literal byte: 20.
        PsdComposite.Unpack([0xFE, 10, 0x00, 20], destination);

        CollectionAssert.AreEqual(new byte[] { 10, 10, 10, 20 }, destination);
    }

    [TestMethod]
    public void Grey_IsDrawnAsItsOwnShade()
    {
        using SKBitmap bitmap = Decode(Psd(1, 2, 1, 8, 1, Raw([0, 200])));

        Assert.AreEqual(new SKColor(0, 0, 0), bitmap.GetPixel(0, 0));
        Assert.AreEqual(new SKColor(200, 200, 200), bitmap.GetPixel(1, 0));
    }

    [TestMethod]
    public void SixteenBitSamples_KeepTheirHighByte()
    {
        // One pixel: red 0x8012, green 0x4000, blue 0xFF12 — the high bytes are 0x80, 0x40 and 0xFF.
        byte[] data = Raw([0x80, 0x12], [0x40, 0x00], [0xFF, 0x12]);

        using SKBitmap bitmap = Decode(Psd(3, 1, 1, 16, 3, data));

        Assert.AreEqual(new SKColor(0x80, 0x40, 0xFF), bitmap.GetPixel(0, 0));
    }

    [TestMethod]
    public void Indexed_LooksTheColourUpInThePalette()
    {
        var palette = new byte[768];
        palette[1] = 10;
        palette[256 + 1] = 20;
        palette[512 + 1] = 30;

        using SKBitmap bitmap = Decode(Psd(1, 1, 1, 8, 2, Raw([1]), palette));

        Assert.AreEqual(new SKColor(10, 20, 30), bitmap.GetPixel(0, 0));
    }

    [TestMethod]
    public void Cmyk_WithNoInkIsWhite()
    {
        // Photoshop stores no ink as 255. Cyan 255, magenta 0, yellow 255, black 128: half-strength red-blue.
        using SKBitmap none = Decode(Psd(4, 1, 1, 8, 4, Raw([255], [255], [255], [255])));
        using SKBitmap half = Decode(Psd(4, 1, 1, 8, 4, Raw([255], [0], [255], [128])));

        Assert.AreEqual(new SKColor(255, 255, 255), none.GetPixel(0, 0));
        Assert.AreEqual(new SKColor(128, 0, 128), half.GetPixel(0, 0));
    }

    [TestMethod]
    public void Version2_TakesEightByteLengthsForTheLayerBlock()
    {
        byte[] file = Psd(1, 1, 1, 8, 1, Raw([77]), version: 2);

        using SKBitmap bitmap = Decode(file);

        Assert.AreEqual(new SKColor(77, 77, 77), bitmap.GetPixel(0, 0));
    }

    [TestMethod]
    public void ReadHeader_ReportsTheSize()
    {
        byte[] file = Psd(3, 7, 5, 8, 3, Raw(new byte[35], new byte[35], new byte[35]));
        using var stream = new MemoryStream(file);

        PsdHeader header = PsdComposite.ReadHeader(stream);

        Assert.AreEqual(7, header.Width);
        Assert.AreEqual(5, header.Height);
        Assert.AreEqual(3, header.Channels);
        Assert.AreEqual(3, header.ColorMode);
    }

    [TestMethod]
    public void NotAPhotoshopFile_IsRefused()
    {
        byte[] file = Psd(3, 1, 1, 8, 3, Raw([0], [0], [0]));
        file[0] = (byte)'X';

        Assert.ThrowsExactly<InvalidDataException>(() => ReadHeaderOf(file));
    }

    [TestMethod]
    public void AnUnknownVersion_IsRefused()
    {
        byte[] file = Psd(3, 1, 1, 8, 3, Raw([0], [0], [0]));
        file[5] = 3;

        Assert.ThrowsExactly<InvalidDataException>(() => ReadHeaderOf(file));
    }

    [TestMethod]
    public void ZipCompression_IsRefused()
    {
        byte[] file = Psd(1, 1, 1, 8, 1, [0, 2, 99]);

        Assert.ThrowsExactly<NotSupportedException>(() => Decode(file));
    }

    [TestMethod]
    public void TheFileEndingEarly_IsRefused()
    {
        byte[] file = Psd(1, 2, 2, 8, 1, Raw([1, 2, 3]));

        Assert.ThrowsExactly<InvalidDataException>(() => Decode(file));
    }

    [TestMethod]
    public void TheDecoder_ShowsTheCompositeOfAFileOnDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.psd");
        try
        {
            File.WriteAllBytes(path, Psd(3, 2, 2, 8, 3, Raw([255, 0, 0, 255], [0, 255, 0, 255], [0, 0, 255, 255])));
            using var decoder = new PsdVideoDecoder(path);
            decoder.Open(TimeSpan.Zero, 2, 2);
            var rgb = new byte[2 * 2 * 3];

            Assert.AreEqual(2, decoder.Info.Width);
            Assert.IsTrue(decoder.ReadFrame(rgb));
            CollectionAssert.AreEqual(new byte[] { 255, 0, 0 }, rgb[..3]);
            Assert.IsFalse(decoder.ReadFrame(rgb), "a picture is one frame");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void ReadHeaderOf(byte[] file)
    {
        using var stream = new MemoryStream(file);
        PsdComposite.ReadHeader(stream);
    }
}
