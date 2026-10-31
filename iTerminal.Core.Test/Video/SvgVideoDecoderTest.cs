using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

/// <summary>An SVG drawing is rasterized into one frame for the picture viewers, without ffmpeg.</summary>
[TestClass]
public class SvgVideoDecoderTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize() => _folder = Path.Combine(Path.GetTempPath(), "SvgVideoDecoderTest_" + Guid.NewGuid().ToString("N")[..8]);

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

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_folder);
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string RedRectangle = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\"><rect width=\"100\" height=\"50\" fill=\"#ff0000\"/></svg>";

    [TestMethod]
    public void Info_IsTheDrawingsOwnSize()
    {
        using var decoder = new SvgVideoDecoder(Write("red.svg", RedRectangle));

        Assert.AreEqual(100, decoder.Info.Width);
        Assert.AreEqual(50, decoder.Info.Height);
        Assert.AreEqual(2.0, decoder.Info.AspectRatio, 0.001);
    }

    [TestMethod]
    public void ReadFrame_FitsTheDrawingIntoTheFrameKeepingItsShape()
    {
        using var decoder = new SvgVideoDecoder(Write("red.svg", RedRectangle));
        decoder.Open(TimeSpan.Zero, 40, 40);
        var rgb = new byte[40 * 40 * 3];

        Assert.IsTrue(decoder.ReadFrame(rgb));

        // 100x50 scaled to 40 columns is 40x20, so it sits in the middle rows and the rows above and below stay black.
        Pixel(rgb, 40, 0, 0).AssertIs(0, 0, 0, "the top row is letterboxed");
        Pixel(rgb, 40, 20, 20).AssertIs(255, 0, 0, "the middle of the drawing is red");
        Pixel(rgb, 40, 39, 39).AssertIs(0, 0, 0, "the bottom row is letterboxed");
    }

    [TestMethod]
    public void ReadFrame_DeliversTheDrawingOnceAndThenEndsTheStream()
    {
        using var decoder = new SvgVideoDecoder(Write("red.svg", RedRectangle));
        decoder.Open(TimeSpan.Zero, 10, 5);
        var rgb = new byte[10 * 5 * 3];

        Assert.IsTrue(decoder.ReadFrame(rgb));
        Assert.IsFalse(decoder.ReadFrame(rgb), "the second read reports the end of the stream");

        decoder.Open(TimeSpan.Zero, 10, 5);
        Assert.IsTrue(decoder.ReadFrame(rgb), "reopening starts the drawing again");
    }

    [TestMethod]
    public void Constructor_RefusesAFileThatIsNotASvgDrawing()
    {
        string path = Write("broken.svg", "this is not a drawing");

        Assert.ThrowsExactly<InvalidOperationException>(() => new SvgVideoDecoder(path));
    }

    [TestMethod]
    public void IsSvgFile_MatchesTheExtensionOnly()
    {
        Assert.IsTrue(SvgVideoDecoder.IsSvgFile("logo.svg"));
        Assert.IsTrue(SvgVideoDecoder.IsSvgFile("LOGO.SVG"));
        Assert.IsFalse(SvgVideoDecoder.IsSvgFile("logo.svgz"));
        Assert.IsFalse(SvgVideoDecoder.IsSvgFile("logo.png"));
        Assert.IsFalse(SvgVideoDecoder.IsSvgFile(string.Empty));
    }

    [TestMethod]
    public void SvgFiles_OpenInTheImageViewer()
    {
        Assert.IsTrue(ImageFileTypes.IsImageFile("diagram.svg"));
    }

    [TestMethod]
    public void TheFactory_NeedsNoFfmpegForASvgDrawing()
    {
        var factory = new PictureVideoDecoderFactory(new FfmpegVideoDecoderFactory());
        Assert.IsTrue(factory.IsAvailable);

        using IVideoDecoder decoder = factory.Create(Write("red.svg", RedRectangle));

        Assert.IsInstanceOfType<SvgVideoDecoder>(decoder);
        Assert.AreEqual(100, decoder.Info.Width);
    }

    private static PixelValue Pixel(byte[] rgb, int columns, int x, int y)
    {
        int offset = (y * columns + x) * 3;
        return new PixelValue(rgb[offset], rgb[offset + 1], rgb[offset + 2]);
    }

    private readonly record struct PixelValue(byte Red, byte Green, byte Blue)
    {
        public void AssertIs(byte red, byte green, byte blue, string because)
        {
            Assert.AreEqual(red, Red, because + " (red)");
            Assert.AreEqual(green, Green, because + " (green)");
            Assert.AreEqual(blue, Blue, because + " (blue)");
        }
    }
}
