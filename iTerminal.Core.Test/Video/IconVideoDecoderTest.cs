using iTerminal.Core.Video;
using SkiaSharp;

namespace iTerminal.Core.Test.Video;

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

    /// <summary>A valid .ico file holding one PNG image of the given size, filled with red.</summary>
    private string WriteRedIcon(string name, int size)
    {
        byte[] png;
        using (var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque))
        {
            bitmap.Erase(new SKColor(255, 0, 0));
            using var image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            png = data.ToArray();
        }

        // ICONDIR (6 bytes), one ICONDIRENTRY (16 bytes), then the PNG itself, which Windows Vista and later icons may hold.
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
            writer.Write((uint)png.Length);
            writer.Write((uint)22);
            writer.Write(png);
        }

        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, stream.ToArray());
        return path;
    }

    [TestMethod]
    public void Info_IsTheIconsOwnSize()
    {
        using var decoder = new IconVideoDecoder(WriteRedIcon("red.ico", 16));

        Assert.AreEqual(16, decoder.Info.Width);
        Assert.AreEqual(16, decoder.Info.Height);
    }

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

    [TestMethod]
    public void Constructor_RefusesAFileThatIsNotAnIcon()
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, "broken.ico");
        File.WriteAllText(path, "this is not an icon");

        Assert.ThrowsExactly<InvalidOperationException>(() => new IconVideoDecoder(path));
    }

    [TestMethod]
    public void IsIconFile_MatchesTheExtensionOnly()
    {
        Assert.IsTrue(IconVideoDecoder.IsIconFile("app.ico"));
        Assert.IsTrue(IconVideoDecoder.IsIconFile("APP.ICO"));
        Assert.IsFalse(IconVideoDecoder.IsIconFile("app.png"));
        Assert.IsFalse(IconVideoDecoder.IsIconFile(string.Empty));
    }

    [TestMethod]
    public void IconFiles_OpenInTheImageViewer()
    {
        Assert.IsTrue(ImageFileTypes.IsImageFile("app.ico"));
    }

    [TestMethod]
    public void TheFactory_NeedsNoFfmpegForAnIcon()
    {
        var factory = new PictureVideoDecoderFactory(new FfmpegVideoDecoderFactory());

        using IVideoDecoder decoder = factory.Create(WriteRedIcon("red.ico", 16));

        Assert.IsInstanceOfType<IconVideoDecoder>(decoder);
    }
}
