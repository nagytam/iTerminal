using iConsole.Media.Video;

namespace iConsole.Media.Video.Test;

/// <summary>An icon file is rasterized into one frame for the picture viewers, without ffmpeg.</summary>
[TestClass]
public class IconVideoDecoderTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize() => _folder = Path.Combine(Path.GetTempPath(), "IconVideoDecoderTest_" + Guid.NewGuid().ToString("N")[..8]);

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }
    }

    /// <summary>
    /// A valid .ico file of the given size, filled with red. The image is a device-independent bitmap (BITMAPINFOHEADER, 32 bits
    /// per pixel, bottom-up, with an opaque AND mask), the form most icons use, rather than an embedded PNG: the PNG-in-ICO path
    /// is the one that crashed the Windows test host.
    /// </summary>
    private string WriteRedIcon(string name, int size)
    {
        // ICONDIR (6 bytes) and one ICONDIRENTRY (16 bytes) put the image at offset 22.
        const int ImageOffset = 22;
        int maskRowBytes = (size + 31) / 32 * 4;
        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)1);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)(40 + size * size * 4 + maskRowBytes * size));
            writer.Write((uint)ImageOffset);

            // BITMAPINFOHEADER: the height counts the colour and the mask rows together.
            writer.Write((uint)40);
            writer.Write(size);
            writer.Write(size * 2);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)0);
            writer.Write((uint)(size * size * 4));
            writer.Write(0);
            writer.Write(0);
            writer.Write((uint)0);
            writer.Write((uint)0);

            // Pixels are blue, green, red, alpha, rows from the bottom up.
            for (int i = 0; i < size * size; i++)
            {
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((byte)255);
                writer.Write((byte)255);
            }

            // The AND mask is all zero: every pixel is opaque.
            writer.Write(new byte[maskRowBytes * size]);
        }

        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Info_IsTheIconsOwnSize()
    {
        using var decoder = new IconVideoDecoder(WriteRedIcon("red.ico", 16));

        Assert.AreEqual(16, decoder.Info.Width);
        Assert.AreEqual(16, decoder.Info.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadFrame_FillsTheFrameWithTheIconAndEndsTheStream()
    {
        using var decoder = new IconVideoDecoder(WriteRedIcon("red.ico", 16));
        decoder.Open(TimeSpan.Zero, 8, 8);
        var rgb = new byte[8 * 8 * 3];

        Assert.IsTrue(decoder.ReadFrame(rgb));

        // A square icon fits the square frame exactly, so every pixel is the icon's red.
        Assert.AreEqual(255, rgb[0], "red");
        Assert.AreEqual(0, rgb[1], "green");
        Assert.AreEqual(0, rgb[2], "blue");
        Assert.IsFalse(decoder.ReadFrame(rgb), "the second read reports the end of the stream");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_RefusesAFileThatIsNotAnIcon()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "broken.ico");
        File.WriteAllText(path, "this is not an icon");

        Assert.ThrowsExactly<InvalidOperationException>(() => new IconVideoDecoder(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsIconFile_MatchesTheExtensionOnly()
    {
        Assert.IsTrue(IconVideoDecoder.IsIconFile("app.ico"));
        Assert.IsTrue(IconVideoDecoder.IsIconFile("APP.ICO"));
        Assert.IsFalse(IconVideoDecoder.IsIconFile("app.png"));
        Assert.IsFalse(IconVideoDecoder.IsIconFile(string.Empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IconFiles_OpenInTheImageViewer()
    {
        Assert.IsTrue(ImageFileTypes.IsImageFile("app.ico"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFactory_NeedsNoFfmpegForAnIcon()
    {
        var factory = new PictureVideoDecoderFactory(new FfmpegVideoDecoderFactory());

        using IVideoDecoder decoder = factory.Create(WriteRedIcon("red.ico", 16));

        Assert.IsInstanceOfType<IconVideoDecoder>(decoder);
    }
}
