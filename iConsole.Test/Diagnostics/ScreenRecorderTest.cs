using iConsole.Data;
using iConsole.Diagnostics;
using iConsole.Display;
using iConsole.Test.Core;

namespace iConsole.Test.Diagnostics;

[TestClass]
public class ScreenRecorderTest
{
    private static void Contains(string actual, string expected) =>
        Assert.IsTrue(actual.Contains(expected, StringComparison.Ordinal), $"Expected the output to contain '{expected}'.");

    private static ScreenBuffer CreateScreen()
    {
        var screen = new ScreenBuffer(12, 3);
        screen.Write(0, new Position(0, 0), new Size(12, 1), new ConxelString("hello", Color.White, Color.Black));
        screen.Write(0, new Position(0, 1), new Size(12, 1), new ConxelString("日本", Color.Yellow, Color.Blue));
        return screen;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToText_HasOneLinePerRow_WithWideCharactersAsTheyLookOnScreen()
    {
        var recorder = new ScreenRecorder();
        recorder.Capture(CreateScreen());

        Assert.AreEqual("hello\n日本\n", recorder.ToText());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToHtml_EscapesText_AndCarriesTheColours()
    {
        var screen = new ScreenBuffer(8, 1);
        screen.Write(0, new Position(0, 0), new Size(8, 1), new ConxelString("<b>&", Color.Yellow, Color.Blue));
        var recorder = new ScreenRecorder();
        recorder.Capture(screen);

        var html = recorder.ToHtml();

        Contains(html, "&lt;b&gt;&amp;");
        Contains(html, "color:#FFFF00");
        Contains(html, "background:#0000FF");
        Contains(html, "<!doctype html>");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToSvg_SizesEachRunByTheColumnsItCovers()
    {
        var recorder = new ScreenRecorder();
        recorder.Capture(CreateScreen());

        var svg = recorder.ToSvg();

        Contains(svg, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"108\" height=\"54\"");
        // 日本 covers four columns, so its text element is four cells wide.
        Contains(svg, "textLength=\"36\"");
        Contains(svg, ">日本</text>");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Capture_CopiesTheFrame_SoLaterDrawingDoesNotChangeTheRecording()
    {
        var screen = CreateScreen();
        var recorder = new ScreenRecorder();
        recorder.Capture(screen);

        screen.Write(0, new Position(0, 0), new Size(12, 1), new ConxelString("changed", Color.White, Color.Black));

        Contains(recorder.ToText(), "hello");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Save_RejectsAnUnknownFormat()
    {
        var recorder = new ScreenRecorder();
        recorder.Capture(CreateScreen());

        Assert.ThrowsExactly<ArgumentException>(() => recorder.Save("screen.png"));
    }
}
