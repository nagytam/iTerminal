using System.Buffers.Binary;
using System.Text;
using iTerminal.Core.Services.Images;

namespace iTerminal.Core.Test.Services.Images;

[TestClass]
public class ImageMetadataWriterTest
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private string _path = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), "imagetext-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string leftover in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
        {
            File.Delete(leftover);
        }
    }

    private static string Value(IReadOnlyList<ImageInfoField> fields, string group, string name) =>
        fields.Single(f => f.Group == group && f.Name == name).Value;

    private static byte[] Png()
    {
        MemoryStream ms = new();
        ms.Write(PngSignature);
        Chunk(ms, "IHDR", [.. Be32(16), .. Be32(16), 8, 2, 0, 0, 0]);
        Chunk(ms, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01]);
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        stream.Write(Be32((uint)data.Length));
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(data);
        stream.Write(Be32(ImageMetadataWriter.Crc32(Encoding.ASCII.GetBytes(type), data)));
    }

    private static byte[] Be32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    /// <summary>A JPEG with an EXIF block that holds only the camera make, then the scan data and the end marker.</summary>
    private static byte[] Jpeg(string make)
    {
        byte[] makeBytes = [.. Encoding.ASCII.GetBytes(make), 0];
        byte[] tiff = new byte[26 + makeBytes.Length];
        "II"u8.CopyTo(tiff);
        tiff[2] = 42;
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), 0x010F);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), (uint)makeBytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(18), 26);
        makeBytes.CopyTo(tiff, 26);

        byte[] exif = [.. "Exif\0\0"u8, .. tiff];
        MemoryStream ms = new();
        ms.Write([0xFF, 0xD8, 0xFF, 0xE1]);
        ms.Write([(byte)((exif.Length + 2) >> 8), (byte)(exif.Length + 2)]);
        ms.Write(exif);
        ms.Write([0xFF, 0xDA, 0x00, 0x03, 0x01, 0x00, 0x00, 0x12, 0x34, 0xFF, 0xD9]);
        return ms.ToArray();
    }

    [TestMethod]
    public void PngText_IsWrittenAndReadBack()
    {
        File.WriteAllBytes(_path + ".png", Png());

        ImageMetadataWriter.Write(_path + ".png", new ImageMetadataEdit("Sunset", "Tamas", "© 2026", "Taken at the lake"));

        IReadOnlyList<ImageInfoField> fields = ImageInfoReader.ReadFile(_path + ".png");
        Assert.AreEqual("Sunset", Value(fields, "Text", "Description"));
        Assert.AreEqual("Tamas", Value(fields, "Text", "Author"));
        Assert.AreEqual("Taken at the lake", Value(fields, "Text", "Comment"));
        Assert.AreEqual("© 2026", Value(fields, "Text", "Copyright"));
    }

    [TestMethod]
    public void PngText_KeepsThePictureUnchanged()
    {
        byte[] original = Png();
        File.WriteAllBytes(_path + ".png", original);

        ImageMetadataWriter.Write(_path + ".png", new ImageMetadataEdit("Sunset", string.Empty, string.Empty, string.Empty));

        byte[] written = File.ReadAllBytes(_path + ".png");
        CollectionAssert.AreEqual(original[..(PngSignature.Length + 25)], written[..(PngSignature.Length + 25)]);
        Assert.IsTrue(Encoding.ASCII.GetString(written).Contains("IDAT"), "the image data is kept");
        CollectionAssert.AreEqual(new byte[] { 0xAE, 0x42, 0x60, 0x82 }, written[^4..], "the end marker and its checksum are kept");
    }

    [TestMethod]
    public void PngText_OutsideLatin1_IsWrittenAsUtf8AndReadBack()
    {
        File.WriteAllBytes(_path + ".png", Png());

        ImageMetadataWriter.Write(_path + ".png", new ImageMetadataEdit("Ünïcode 日本", string.Empty, string.Empty, string.Empty));

        Assert.AreEqual("Ünïcode 日本", Value(ImageInfoReader.ReadFile(_path + ".png"), "Text", "Description"));
    }

    [TestMethod]
    public void PngText_ASecondEditReplacesTheFirst()
    {
        File.WriteAllBytes(_path + ".png", Png());
        ImageMetadataWriter.Write(_path + ".png", new ImageMetadataEdit("First", string.Empty, string.Empty, string.Empty));

        ImageMetadataWriter.Write(_path + ".png", new ImageMetadataEdit("Second", string.Empty, string.Empty, string.Empty));

        IReadOnlyList<ImageInfoField> fields = ImageInfoReader.ReadFile(_path + ".png");
        Assert.AreEqual("Second", Value(fields, "Text", "Description"));
        Assert.AreEqual(1, fields.Count(f => f.Group == "Text" && f.Name == "Description"));
    }

    [TestMethod]
    public void PngText_AnEmptyFieldRemovesItsChunk()
    {
        File.WriteAllBytes(_path + ".png", Png());
        ImageMetadataWriter.Write(_path + ".png", new ImageMetadataEdit("Sunset", "Tamas", string.Empty, string.Empty));

        ImageMetadataWriter.Write(_path + ".png", ImageMetadataEdit.Empty);

        Assert.IsFalse(ImageInfoReader.ReadFile(_path + ".png").Any(f => f.Group == "Text"));
    }

    [TestMethod]
    public void Jpeg_ExifTextAndComment_AreWrittenAndTheCameraMakeIsKept()
    {
        File.WriteAllBytes(_path + ".jpg", Jpeg("Acme"));

        ImageMetadataWriter.Write(_path + ".jpg", new ImageMetadataEdit("Sunset", "Tamas", "© 2026", "Lake"));

        IReadOnlyList<ImageInfoField> fields = ImageInfoReader.ReadFile(_path + ".jpg");
        Assert.AreEqual("Sunset", Value(fields, "Image", "Description"));
        Assert.AreEqual("Tamas", Value(fields, "Image", "Artist"));
        Assert.AreEqual("© 2026", Value(fields, "Image", "Copyright"));
        Assert.AreEqual("Lake", Value(fields, "Text", "Comment"));
        Assert.AreEqual("Acme", Value(fields, "Camera", "Make"));
    }

    [TestMethod]
    public void Jpeg_EmptyFieldsRemoveTheirTags_AndTheScanIsKept()
    {
        byte[] original = Jpeg("Acme");
        File.WriteAllBytes(_path + ".jpg", original);
        ImageMetadataWriter.Write(_path + ".jpg", new ImageMetadataEdit("Sunset", "Tamas", string.Empty, "Lake"));

        ImageMetadataWriter.Write(_path + ".jpg", new ImageMetadataEdit(string.Empty, string.Empty, string.Empty, string.Empty));

        byte[] written = File.ReadAllBytes(_path + ".jpg");
        IReadOnlyList<ImageInfoField> fields = ImageInfoReader.ReadFile(_path + ".jpg");
        Assert.IsFalse(fields.Any(f => f.Name is "Description" or "Artist" or "Comment"));
        Assert.AreEqual("Acme", Value(fields, "Camera", "Make"));
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0xFF, 0xD9 }, written[^4..]);
    }

    [TestMethod]
    public void Jpeg_WithNoExif_GetsOne()
    {
        byte[] bare = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x03, 0x01, 0x00, 0x00, 0x12, 0x34, 0xFF, 0xD9];
        File.WriteAllBytes(_path + ".jpeg", bare);

        ImageMetadataWriter.Write(_path + ".jpeg", new ImageMetadataEdit("Sunset", string.Empty, string.Empty, string.Empty));

        Assert.AreEqual("Sunset", Value(ImageInfoReader.ReadFile(_path + ".jpeg"), "Image", "Description"));
    }

    [TestMethod]
    public void AJpegWithNoImageData_IsRefused_AndNotTouched()
    {
        byte[] damaged = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00];
        File.WriteAllBytes(_path + ".jpg", damaged);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ImageMetadataWriter.Write(_path + ".jpg", ImageMetadataEdit.Empty));
        CollectionAssert.AreEqual(damaged, File.ReadAllBytes(_path + ".jpg"));
    }

    [TestMethod]
    public void AGif_IsRefused_AndNotTouched()
    {
        byte[] gif = Encoding.ASCII.GetBytes("GIF89a");
        File.WriteAllBytes(_path + ".gif", gif);

        Assert.ThrowsExactly<NotSupportedException>(() =>
            ImageMetadataWriter.Write(_path + ".gif", ImageMetadataEdit.Empty));
        CollectionAssert.AreEqual(gif, File.ReadAllBytes(_path + ".gif"));
    }

    [TestMethod]
    public void CanWrite_IsTrueOnlyForJpegAndPng()
    {
        Assert.IsTrue(ImageMetadataWriter.CanWrite("a.JPG"));
        Assert.IsTrue(ImageMetadataWriter.CanWrite("a.jpeg"));
        Assert.IsTrue(ImageMetadataWriter.CanWrite("a.png"));
        Assert.IsFalse(ImageMetadataWriter.CanWrite("a.gif"));
        Assert.IsFalse(ImageMetadataWriter.CanWrite("a.bmp"));
    }

    [TestMethod]
    public void From_TakesTheEditableTextFromTheFieldsWhereverTheFormatKeepsIt()
    {
        ImageInfoField[] fields =
        [
            new("Image", "Artist", "Tamas"),
            new("Text", "Author", "ignored, the EXIF artist wins"),
            new("Text", "Copyright", "© 2026"),
            new("Text", "Description", "Sunset"),
            new("Text", "Comment", "Lake"),
        ];

        ImageMetadataEdit edit = ImageMetadataEdit.From(fields);

        Assert.AreEqual(new ImageMetadataEdit("Sunset", "Tamas", "© 2026", "Lake"), edit);
    }

    [TestMethod]
    public void From_TakesThePngAuthorAsTheArtistWhenThereIsNoExif()
    {
        ImageMetadataEdit edit = ImageMetadataEdit.From([new("Text", "Author", "Tamas")]);

        Assert.AreEqual("Tamas", edit.Artist);
        Assert.AreEqual(string.Empty, edit.Description);
    }

    [TestMethod]
    public void Strip_JpegLosesItsExifAndComments_AndKeepsThePicture()
    {
        File.WriteAllBytes(_path + ".jpg", Jpeg("Acme"));
        ImageMetadataWriter.Write(_path + ".jpg", new ImageMetadataEdit("Sunset", "Tamas", string.Empty, "Lake"));

        bool changed = ImageMetadataWriter.Strip(_path + ".jpg");

        Assert.IsTrue(changed);
        IReadOnlyList<ImageInfoField> fields = ImageInfoReader.ReadFile(_path + ".jpg");
        Assert.IsFalse(fields.Any(f => f.Group == "Camera"), "the camera make is gone");
        Assert.IsFalse(fields.Any(f => f.Group == "Text"), "the comment is gone");
        Assert.IsFalse(fields.Any(f => f.Name is "Description" or "Artist" or "Copyright"), "the EXIF text is gone");
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0xFF, 0xD9 }, File.ReadAllBytes(_path + ".jpg")[^4..]);
    }

    [TestMethod]
    public void Strip_PngLosesItsTextChunks_AndAFileWithNoneIsLeftAlone()
    {
        File.WriteAllBytes(_path + ".png", Png());
        ImageMetadataWriter.Write(_path + ".png", new ImageMetadataEdit("Sunset", "Tamas", string.Empty, string.Empty));

        Assert.IsTrue(ImageMetadataWriter.Strip(_path + ".png"));
        Assert.IsFalse(ImageInfoReader.ReadFile(_path + ".png").Any(f => f.Group == "Text"));

        byte[] stripped = File.ReadAllBytes(_path + ".png");
        Assert.IsFalse(ImageMetadataWriter.Strip(_path + ".png"));
        CollectionAssert.AreEqual(stripped, File.ReadAllBytes(_path + ".png"));
    }

    [TestMethod]
    public void Strip_AFileWithNothingToRemove_IsNotRewritten()
    {
        File.WriteAllBytes(_path + ".jpg", [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x03, 0x01, 0x00, 0x00, 0x12, 0x34, 0xFF, 0xD9]);
        DateTime before = File.GetLastWriteTimeUtc(_path + ".jpg");

        Assert.IsFalse(ImageMetadataWriter.Strip(_path + ".jpg"));
        Assert.AreEqual(before, File.GetLastWriteTimeUtc(_path + ".jpg"));
    }

    [TestMethod]
    public void Strip_OtherFormatsAreRefused()
    {
        File.WriteAllBytes(_path + ".gif", Encoding.ASCII.GetBytes("GIF89a"));

        Assert.ThrowsExactly<NotSupportedException>(() => ImageMetadataWriter.Strip(_path + ".gif"));
    }
}
