using System.IO.Compression;
using System.Text;
using iTerminal.Core.Documents.Extractors;

namespace iTerminal.Core.Test.Documents;

[TestClass]
public class ImageMetadataReaderTest
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "image-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_directory, true); } catch { /* best effort */ }
    }

    private string Save(byte[] bytes)
    {
        string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".img");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] Concat(params byte[][] parts) => [.. parts.SelectMany(p => p)];

    private static byte[] Latin1(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Zlib(byte[] data)
    {
        using MemoryStream output = new();
        using (ZLibStream z = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] PngChunk(string type, byte[] data)
    {
        byte[] length = [(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length];
        return Concat(length, Latin1(type), data, new byte[4]);
    }

    private static byte[] Png(params byte[][] chunks) =>
        Concat([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], Concat(chunks));

    private static byte[] TextChunk(string keyword, string value) =>
        PngChunk("tEXt", Concat(Latin1(keyword), [0], Latin1(value)));

    private static byte[] JpegSegment(byte marker, byte[] data)
    {
        int length = data.Length + 2;
        return Concat([0xFF, marker, (byte)(length >> 8), (byte)length], data);
    }

    private static byte[] Jpeg(params byte[][] segments) =>
        Concat([0xFF, 0xD8], Concat(segments), [0xFF, 0xDA, 0x00, 0x02]);

    /// <summary>One TIFF with an IFD0 of (tag, type, payload) entries; payloads over four bytes go after the directory.</summary>
    private static byte[] Exif(bool littleEndian, params (int Tag, int Type, byte[] Payload)[] entries)
    {
        byte[] U16(int v) => littleEndian ? [(byte)v, (byte)(v >> 8)] : [(byte)(v >> 8), (byte)v];
        byte[] U32(int v) => littleEndian
            ? [(byte)v, (byte)(v >> 8), (byte)(v >> 16), (byte)(v >> 24)]
            : [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

        int dataStart = 8 + 2 + (entries.Length * 12) + 4;
        List<byte> directory = [];
        List<byte> extra = [];
        directory.AddRange(U16(entries.Length));
        foreach ((int tag, int type, byte[] payload) in entries)
        {
            directory.AddRange(U16(tag));
            directory.AddRange(U16(type));
            directory.AddRange(U32(payload.Length));
            if (payload.Length <= 4)
            {
                directory.AddRange(payload);
                directory.AddRange(new byte[4 - payload.Length]);
            }
            else
            {
                directory.AddRange(U32(dataStart + extra.Count));
                extra.AddRange(payload);
            }
        }

        directory.AddRange(new byte[4]);
        byte[] header = Concat(Latin1(littleEndian ? "II" : "MM"), U16(42), U32(8));
        return Concat(header, [.. directory], [.. extra]);
    }

    private static byte[] ExifSegment(byte[] tiff) => JpegSegment(0xE1, Concat(Latin1("Exif\0\0"), tiff));

    private static byte[] XmpSegment(string xmp) =>
        JpegSegment(0xE1, Concat(Latin1("http://ns.adobe.com/xap/1.0/\0"), Encoding.UTF8.GetBytes(xmp)));

    private static byte[] Ascii(string text) => Concat(Encoding.UTF8.GetBytes(text), [0]);

    private static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text + "\0");

    [Timeout(60_000)]
    [TestMethod]
    public void MissingFile_AndUnknownFormats_AreEmpty()
    {
        Assert.IsTrue(ImageMetadataReader.Read(Path.Combine(_directory, "missing.png")).IsEmpty);
        Assert.IsTrue(ImageMetadataReader.Read(Save(Latin1("GIF89a-not-handled"))).IsEmpty);
        Assert.IsTrue(ImageMetadataReader.Read(Save([])).IsEmpty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Png_ReadsTitleAuthorAndLabelledDescriptions()
    {
        string path = Save(Png(
            PngChunk("IHDR", new byte[13]),
            TextChunk("Title", "Sunset"),
            TextChunk("Title", "Ignored second title"),
            TextChunk("Artist", "Ada"),
            TextChunk("Author", "Ignored second author"),
            TextChunk("Software", "GIMP"),
            TextChunk("Comment", "   "),
            PngChunk("tEXt", Latin1("nokeyword")),
            PngChunk("IDAT", new byte[4]),
            TextChunk("Title", "After the pixels")));

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);

        Assert.AreEqual("Sunset", metadata.Title);
        Assert.AreEqual("Ada", metadata.Author);
        Assert.AreEqual("Software: GIMP", metadata.Description);
        Assert.AreEqual("Sunset\nAda\nSoftware: GIMP", metadata.ToText());
        Assert.IsFalse(metadata.IsEmpty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Png_ReadsCompressedText()
    {
        string path = Save(Png(
            PngChunk("zTXt", Concat(Latin1("Description"), [0, 0], Zlib(Latin1("packed words")))),
            PngChunk("zTXt", Concat(Latin1("Broken"), [0, 0], Latin1("not zlib"))),
            PngChunk("zTXt", Concat(Latin1("NoData"), [0, 0])),
            PngChunk("zTXt", Latin1("nokeyword")),
            PngChunk("IEND", [])));

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);

        Assert.AreEqual("Description: packed words", metadata.Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Png_ReadsInternationalText_PlainAndCompressed()
    {
        byte[] Itxt(string keyword, bool compressed, string value) => PngChunk("iTXt", Concat(
            Latin1(keyword), [0, (byte)(compressed ? 1 : 0), 0], Latin1("en"), [0], Latin1("tk"), [0],
            compressed ? Zlib(Encoding.UTF8.GetBytes(value)) : Encoding.UTF8.GetBytes(value)));

        string path = Save(Png(
            Itxt("Title", false, "Árvíztűrő"),
            Itxt("Author", true, "Béla"),
            Itxt("Note", true, "x"),
            PngChunk("iTXt", Concat(Latin1("BadZ"), [0, 1, 0, 0, 0], Latin1("garbage"))),
            PngChunk("iTXt", Concat(Latin1("NoTerminators"), [0, 0, 0], Latin1("en"))),
            PngChunk("iTXt", Concat(Latin1("NoText"), [0, 0, 0, 0, 0])),
            PngChunk("iTXt", Latin1("short"))));

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);

        Assert.AreEqual("Árvíztűrő", metadata.Title);
        Assert.AreEqual("Béla", metadata.Author);
        StringAssert.Contains(metadata.Description, "Note: x");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Png_WithTruncatedChunk_StopsCleanly()
    {
        byte[] truncated = Concat(Png(TextChunk("Title", "Kept")), [0x7F, 0xFF, 0xFF, 0xFF, (byte)'t', (byte)'E', (byte)'X', (byte)'t']);

        Assert.AreEqual("Kept", ImageMetadataReader.Read(Save(truncated)).Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_ReadsCommentsAndSkipsOtherSegments()
    {
        string path = Save(Jpeg(
            JpegSegment(0xE0, Latin1("JFIF")),
            JpegSegment(0xFE, Latin1("  a comment  ")),
            JpegSegment(0xFE, Latin1("   ")),
            JpegSegment(0xE1, Latin1("short")),
            JpegSegment(0xE1, Latin1("Something else entirely that is long enough to pass the length check"))));

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);

        Assert.AreEqual("a comment", metadata.Description);
        Assert.AreEqual(string.Empty, metadata.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Jpeg_ReadsExifStrings_InEitherByteOrder(bool littleEndian)
    {
        string path = Save(Jpeg(ExifSegment(Exif(
            littleEndian,
            (0x010E, 2, Ascii("A long image description")),
            (0x013B, 2, Ascii("Zed")),
            (0x9C9B, 1, Utf16("Windows title")),
            (0x9C9C, 1, Utf16("Windows comment")),
            (0x0100, 3, [1, 0, 0, 0])))));

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);

        Assert.AreEqual("Windows title", metadata.Title);
        Assert.AreEqual("Zed", metadata.Author);
        Assert.AreEqual("A long image description", metadata.Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_ExifWindowsCommentAndSubject_FillAMissingDescription()
    {
        string path = Save(Jpeg(ExifSegment(Exif(
            true,
            (0x9C9C, 1, Utf16("Windows comment")),
            (0x9C9E, 1, Utf16("Ignored later"))))));

        Assert.AreEqual("Windows comment", ImageMetadataReader.Read(path).Description);

        string subject = Save(Jpeg(ExifSegment(Exif(true, (0x9C9E, 1, Utf16("Keywords"))))));
        Assert.AreEqual("Keywords", ImageMetadataReader.Read(subject).Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_ExifWithShortInlineValueAndUnreadableValues_AreHandled()
    {
        string path = Save(Jpeg(ExifSegment(Exif(
            true,
            (0x013B, 2, Ascii("Al")),
            (0x010E, 3, [1, 0, 0, 0]),
            (0x9C9B, 1, [])))));

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);

        Assert.AreEqual("Al", metadata.Author);
        Assert.AreEqual(string.Empty, metadata.Title);
        Assert.AreEqual(string.Empty, metadata.Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_ExifWithUnterminatedAsciiAndBadOffsets_AreHandled()
    {
        byte[] tiff = Exif(true, (0x010E, 2, Latin1("unterminated text")));
        Assert.AreEqual("unterminated text", ImageMetadataReader.Read(Save(Jpeg(ExifSegment(tiff)))).Description);

        byte[] badOffset = Exif(true, (0x010E, 2, Ascii("A long image description")));
        badOffset[8 + 2 + 8] = 0xFF;
        badOffset[8 + 2 + 9] = 0xFF;
        Assert.AreEqual(string.Empty, ImageMetadataReader.Read(Save(Jpeg(ExifSegment(badOffset)))).Description);

        byte[] tinyIfd = Concat(Latin1("II"), [42, 0, 2, 0, 0, 0], [0, 0]);
        Assert.IsTrue(ImageMetadataReader.Read(Save(Jpeg(ExifSegment(tinyIfd)))).IsEmpty);

        byte[] tooShort = Latin1("II*\0");
        Assert.IsTrue(ImageMetadataReader.Read(Save(Jpeg(ExifSegment(tooShort)))).IsEmpty);

        byte[] hugeCount = Exif(true, (0x010E, 2, Ascii("A long image description")));
        hugeCount[8 + 2 + 4] = 0xFF;
        hugeCount[8 + 2 + 5] = 0xFF;
        Assert.AreEqual(string.Empty, ImageMetadataReader.Read(Save(Jpeg(ExifSegment(hugeCount)))).Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_ExifDirectoryRunningPastTheSegment_StopsCleanly()
    {
        byte[] tiff = Exif(true, (0x013B, 2, Ascii("Ok")));
        tiff[8] = 50; // entry count far beyond the bytes present
        Assert.AreEqual("Ok", ImageMetadataReader.Read(Save(Jpeg(ExifSegment(tiff)))).Author);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_ReadsXmpDublinCore()
    {
        const string xmp = """
            <x:xmpmeta><rdf:RDF><rdf:Description>
            <dc:title><rdf:Alt><rdf:li xml:lang="x-default">Xmp title</rdf:li></rdf:Alt></dc:title>
            <dc:creator><rdf:Seq><rdf:li>Xmp author</rdf:li></rdf:Seq></dc:creator>
            <dc:description><rdf:Alt><rdf:li>Xmp description</rdf:li></rdf:Alt></dc:description>
            </rdf:Description></rdf:RDF></x:xmpmeta>
            """;

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(Save(Jpeg(XmpSegment(xmp))));

        Assert.AreEqual("Xmp title", metadata.Title);
        Assert.AreEqual("Xmp author", metadata.Author);
        Assert.AreEqual("Xmp description", metadata.Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_XmpWithUnclosedElement_IsIgnored()
    {
        Assert.IsTrue(ImageMetadataReader.Read(Save(Jpeg(XmpSegment("<dc:title><rdf:li>never closed")))).IsEmpty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_ExifWinsOverLaterXmp_ForTitleAndAuthor()
    {
        string path = Save(Jpeg(
            ExifSegment(Exif(true, (0x013B, 2, Ascii("Exif artist")), (0x9C9B, 1, Utf16("Exif title")))),
            XmpSegment("<dc:title>Xmp title</dc:title><dc:creator>Xmp author</dc:creator>")));

        ImageMetadataReader.ImageMetadata metadata = ImageMetadataReader.Read(path);

        Assert.AreEqual("Exif title", metadata.Title);
        Assert.AreEqual("Exif artist", metadata.Author);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Jpeg_BrokenSegmentStructure_StopsCleanly()
    {
        Assert.IsTrue(ImageMetadataReader.Read(Save([0xFF, 0xD8, 0x00, 0x00, 0x00, 0x00])).IsEmpty);
        Assert.IsTrue(ImageMetadataReader.Read(Save([0xFF, 0xD8, 0xFF, 0xFE, 0x00, 0x01])).IsEmpty);
        Assert.IsTrue(ImageMetadataReader.Read(Save([0xFF, 0xD8, 0xFF, 0xFE, 0xFF, 0xFF, 0x41])).IsEmpty);
        Assert.IsTrue(ImageMetadataReader.Read(Save([0xFF, 0xD8, 0xFF, 0xD9, 0x00, 0x00])).IsEmpty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Metadata_Empty_HasNoText()
    {
        Assert.IsTrue(ImageMetadataReader.ImageMetadata.Empty.IsEmpty);
        Assert.AreEqual(string.Empty, ImageMetadataReader.ImageMetadata.Empty.ToText());
    }
}
