using iConsole.Display;
using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Video;

[TestClass]
public class AsciiArtTest
{
    private static byte[] Pixels(params (byte R, byte G, byte B)[] pixels) =>
        [.. pixels.SelectMany(p => new[] { p.R, p.G, p.B })];

    [Timeout(60_000)]
    [TestMethod]
    public void Render_MapsDarkToBlankAndBrightToDense()
    {
        var frame = new AsciiFrame(3, 1);

        AsciiArt.Render(Pixels((0, 0, 0), (128, 128, 128), (255, 255, 255)), frame, AsciiColorMode.Mono);

        Assert.AreEqual(' ', frame.Characters[0]);
        Assert.AreEqual('$', frame.Characters[2]);
        Assert.IsTrue(AsciiArt.Ramp.IndexOf(frame.Characters[1]) > 0
            && AsciiArt.Ramp.IndexOf(frame.Characters[1]) < AsciiArt.Ramp.Length - 1);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_MonoUsesOnlyGreyAndWhite()
    {
        var frame = new AsciiFrame(2, 1);

        AsciiArt.Render(Pixels((255, 0, 0), (10, 10, 10)), frame, AsciiColorMode.Mono);

        Assert.IsTrue(frame.Foregrounds.All(c => c is Color.White or Color.Gray));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_ColourModePicksTheHue()
    {
        var frame = new AsciiFrame(4, 1);

        AsciiArt.Render(Pixels((255, 0, 0), (0, 255, 0), (0, 0, 255), (255, 255, 0)), frame, AsciiColorMode.Color);

        Assert.AreEqual(Color.Red, frame.Foregrounds[0]);
        Assert.AreEqual(Color.Green, frame.Foregrounds[1]);
        Assert.AreEqual(Color.Blue, frame.Foregrounds[2]);
        Assert.AreEqual(Color.Yellow, frame.Foregrounds[3]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorOf_DimColoursUseTheDarkVariantAndGreyStaysGrey()
    {
        Assert.AreEqual(Color.DarkRed, AsciiArt.ColorOf(100, 0, 0));
        Assert.AreEqual(Color.White, AsciiArt.ColorOf(250, 250, 250));
        Assert.AreEqual(Color.DarkGray, AsciiArt.ColorOf(5, 5, 5));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Render_ThrowsWhenThePixelsAreTooFew()
    {
        var frame = new AsciiFrame(2, 2);

        Assert.ThrowsExactly<ArgumentException>(() => AsciiArt.Render(new byte[3], frame, AsciiColorMode.Mono));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Fit_AccountsForTallCells()
    {
        // A 16:9 picture on a wide room: 100 columns need 100 / (16/9) / 2 ≈ 28 rows.
        var (columns, rows) = AsciiArt.Fit(16.0 / 9.0, 100, 50);

        Assert.AreEqual(100, columns);
        Assert.AreEqual(28, rows);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Fit_ShrinksTheWidthWhenTheRoomIsShort()
    {
        var (columns, rows) = AsciiArt.Fit(16.0 / 9.0, 200, 20);

        Assert.AreEqual(20, rows);
        Assert.IsTrue(columns < 200);
        Assert.AreEqual(71, columns);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Fit_NeverReturnsMoreThanTheRoom()
    {
        var (columns, rows) = AsciiArt.Fit(0.1, 10, 4);

        Assert.IsTrue(columns >= 1 && columns <= 10);
        Assert.IsTrue(rows >= 1 && rows <= 4);
        Assert.AreEqual((0, 0), AsciiArt.Fit(1, 0, 5));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseProbe_ReadsSizeRateAndDuration()
    {
        const string json = """
            {"streams":[{"width":1920,"height":1080,"r_frame_rate":"30000/1001","avg_frame_rate":"30000/1001","duration":"12.5"}],
             "format":{"duration":"13.0"}}
            """;

        var info = FfmpegVideoDecoder.ParseProbe(json);

        Assert.AreEqual(1920, info.Width);
        Assert.AreEqual(1080, info.Height);
        Assert.AreEqual(29.97, info.FrameRate, 0.01);
        Assert.AreEqual(TimeSpan.FromSeconds(12.5), info.Duration);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseProbe_FallsBackToTheContainerDurationAndAStillImageRate()
    {
        const string json = """
            {"streams":[{"width":64,"height":32,"r_frame_rate":"25/1","avg_frame_rate":"0/0"}],"format":{"duration":"4.0"}}
            """;

        var info = FfmpegVideoDecoder.ParseProbe(json);

        Assert.AreEqual(25, info.FrameRate, 0.001);
        Assert.AreEqual(TimeSpan.FromSeconds(4), info.Duration);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseProbe_ThrowsWithoutAVideoStream()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => FfmpegVideoDecoder.ParseProbe("""{"streams":[]}"""));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileTypes_RecogniseVideosAndPicturesButNotTypeScript()
    {
        Assert.IsTrue(VideoFileTypes.IsVideoFile("/a/clip.MKV"));
        Assert.IsFalse(VideoFileTypes.IsVideoFile("/a/app.ts"));
        Assert.IsTrue(ImageFileTypes.IsImageFile("photo.JPG"));
        Assert.IsFalse(ImageFileTypes.IsImageFile("notes.txt"));
    }
}
