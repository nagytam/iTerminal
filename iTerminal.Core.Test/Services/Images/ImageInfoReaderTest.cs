using System.Buffers.Binary;
using System.Text;
using iTerminal.Core.Services.Images;

namespace iTerminal.Core.Test.Services.Images;

[TestClass]
public class ImageInfoReaderTest
{
    private static string Value(IReadOnlyList<ImageInfoField> fields, string name) =>
        fields.Single(f => f.Name == name).Value;

    private static byte[] Png(params (string Keyword, string Text)[] texts)
    {
        var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(ms, "IHDR", Be32(640).Concat(Be32(480)).Concat(new byte[] { 8, 2, 0, 0, 0 }).ToArray());
        foreach (var (keyword, text) in texts)
        {
            Chunk(ms, "tEXt", Encoding.Latin1.GetBytes(keyword + "\0" + text));
        }

        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        stream.Write(Be32((uint)data.Length));
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(new byte[4]);
    }

    private static byte[] Be32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>
    /// A little-endian TIFF EXIF block: IFD0 (make, model, and the pointers to EXIF and GPS), the EXIF IFD (exposure, f-number,
    /// ISO, the date taken), and the GPS IFD (latitude and longitude as degrees, minutes and seconds).
    /// </summary>
    private static byte[] Tiff()
    {
        byte[] t = new byte[300];
        "II"u8.CopyTo(t);
        t[2] = 42;
        BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(4), 8);

        // IFD0 at 8: Make (ASCII at 170), Model (ASCII at 175), ExifIFD (62), GPSIFD (116).
        Entry(t, 8, 0x010F, 2, 5, 170);
        Entry(t, 8, 0x0110, 2, 7, 175);
        Entry(t, 8, 0x8769, 4, 1, 62);
        Entry(t, 8, 0x8825, 4, 1, 116);

        // EXIF IFD at 62: ExposureTime (182), FNumber (190), ISO (inline), DateTimeOriginal (198).
        Entry(t, 62, 0x829A, 5, 1, 182);
        Entry(t, 62, 0x829D, 5, 1, 190);
        Entry(t, 62, 0x8827, 3, 1, 400);
        Entry(t, 62, 0x9003, 2, 20, 198);

        // GPS IFD at 116: N (inline), latitude (218), E (inline), longitude (242).
        Entry(t, 116, 0x0001, 2, 2, Inline("N"));
        Entry(t, 116, 0x0002, 5, 3, 218);
        Entry(t, 116, 0x0003, 2, 2, Inline("E"));
        Entry(t, 116, 0x0004, 5, 3, 242);

        Ascii(t, 170, "Acme");
        Ascii(t, 175, "Zed100");
        Rational(t, 182, 1, 125);
        Rational(t, 190, 28, 10);
        Ascii(t, 198, "2024:05:01 10:20:30");
        Dms(t, 218, 47, 22, 3684, 100);
        Dms(t, 242, 8, 32, 30, 1);
        return t;
    }

    /// <summary>An inline value is written in the entry itself: its first bytes are the value, so the offset slot holds them.</summary>
    private static uint Inline(string text) => text[0];

    private static void Entry(byte[] t, int ifd, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        int at = ifd + 2 + IfdEntries(t, ifd) * 12;
        BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(ifd), (ushort)(IfdEntries(t, ifd) + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(at), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(at + 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(at + 4), count);
        if (type == 3)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(t.AsSpan(at + 8), (ushort)valueOrOffset);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(at + 8), valueOrOffset);
        }
    }

    private static int IfdEntries(byte[] t, int ifd) => BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(ifd));

    private static void Ascii(byte[] t, int at, string text)
    {
        Encoding.ASCII.GetBytes(text).CopyTo(t, at);
    }

    private static void Rational(byte[] t, int at, uint numerator, uint denominator)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(at), numerator);
        BinaryPrimitives.WriteUInt32LittleEndian(t.AsSpan(at + 4), denominator);
    }

    private static void Dms(byte[] t, int at, uint degrees, uint minutes, uint seconds, uint secondsDenominator)
    {
        Rational(t, at, degrees, 1);
        Rational(t, at + 8, minutes, 1);
        Rational(t, at + 16, seconds, secondsDenominator);
    }

    private static byte[] Jpeg()
    {
        byte[] tiff = Tiff();
        var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        // APP1: the "Exif" header, then the TIFF block; its length counts itself and the header.
        ms.Write([0xFF, 0xE1]);
        ms.Write([(byte)((tiff.Length + 8) >> 8), (byte)(tiff.Length + 8)]);
        ms.Write("Exif\0\0"u8);
        ms.Write(tiff);
        // SOF0: 8-bit precision, height 1000, width 1500.
        ms.Write([0xFF, 0xC0, 0x00, 0x11, 0x08, 0x03, 0xE8, 0x05, 0xDC, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00]);
        ms.Write([0xFF, 0xDA, 0x00, 0x02]);
        return ms.ToArray();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Png_GivesItsFormatDimensionsAndTextChunks()
    {
        var fields = ImageInfoReader.Read(Png(("Title", "Sunset")), 1234);

        Assert.AreEqual("PNG", Value(fields, "Format"));
        Assert.AreEqual("640 × 480 pixels", Value(fields, "Dimensions"));
        Assert.AreEqual("Sunset", Value(fields, "Title"));
        Assert.AreEqual("1,234 bytes", Value(fields, "Size"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_GivesTheCameraExposureDateAndPosition()
    {
        var fields = ImageInfoReader.Read(Jpeg(), 5000);

        Assert.AreEqual("JPEG", Value(fields, "Format"));
        Assert.AreEqual("1500 × 1000 pixels", Value(fields, "Dimensions"));
        Assert.AreEqual("Acme", Value(fields, "Make"));
        Assert.AreEqual("Zed100", Value(fields, "Model"));
        Assert.AreEqual("1/125 s", Value(fields, "Exposure"));
        Assert.AreEqual("f/2.8", Value(fields, "Aperture"));
        Assert.AreEqual("400", Value(fields, "ISO"));
        Assert.AreEqual("2024-05-01 10:20:30", Value(fields, "Taken"));
        Assert.AreEqual("47.376900, 8.541667", Value(fields, "Position"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EachFieldKnowsItsGroup()
    {
        var fields = ImageInfoReader.Read(Jpeg(), 5000);

        Assert.AreEqual("Camera", fields.Single(f => f.Name == "Make").Group);
        Assert.AreEqual("Capture", fields.Single(f => f.Name == "Taken").Group);
        Assert.AreEqual("Location", fields.Single(f => f.Name == "Position").Group);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFileThatIsNoImage_SaysSoAndKeepsItsSize()
    {
        var fields = ImageInfoReader.Read("just text"u8, 9);

        Assert.AreEqual("Unknown", Value(fields, "Format"));
        Assert.AreEqual("9 bytes", Value(fields, "Size"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ATruncatedFile_ReadsWhatIsThere()
    {
        byte[] jpeg = Jpeg();

        var fields = ImageInfoReader.Read(jpeg.AsSpan(0, 12), 12);

        Assert.AreEqual("JPEG", Value(fields, "Format"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AMissingFile_GivesAnErrorField()
    {
        var fields = ImageInfoReader.ReadFile(Path.Combine(Path.GetTempPath(), "no-such-image-" + Guid.NewGuid().ToString("N") + ".jpg"));

        Assert.AreEqual("File", fields.Single().Group);
        Assert.AreEqual("Error", fields.Single().Name);
    }
}
