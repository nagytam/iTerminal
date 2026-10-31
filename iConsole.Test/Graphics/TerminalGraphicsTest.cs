using iConsole.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Graphics;

[TestClass]
public class TerminalGraphicsTest
{
    private static Func<string, string?> Env(params (string Name, string Value)[] variables) =>
        name => variables.Where(v => v.Name == name).Select(v => v.Value).FirstOrDefault();

    [TestMethod]
    public void DetectSixel_WindowsTerminal_OnWindows()
    {
        Assert.IsTrue(TerminalGraphics.DetectSixel(Env(("WT_SESSION", "abc")), isWindows: true));
    }

    [TestMethod]
    public void DetectSixel_PlainWindowsConsole_IsNotSupported()
    {
        Assert.IsFalse(TerminalGraphics.DetectSixel(Env(), isWindows: true));
    }

    [TestMethod]
    [DataRow("TERM", "foot")]
    [DataRow("TERM", "mlterm")]
    [DataRow("TERM", "xterm-sixel")]
    [DataRow("TERM_PROGRAM", "WezTerm")]
    [DataRow("TERM_PROGRAM", "iTerm.app")]
    [DataRow("KONSOLE_VERSION", "230800")]
    public void DetectSixel_TerminalsThatDrawSixel_OnLinux(string name, string value)
    {
        Assert.IsTrue(TerminalGraphics.DetectSixel(Env((name, value)), isWindows: false));
    }

    [TestMethod]
    [DataRow("TERM", "xterm-256color")]
    [DataRow("TERM", "xterm-kitty")]
    [DataRow("TERM_PROGRAM", "vscode")]
    [DataRow("KONSOLE_VERSION", "210400")]
    public void DetectSixel_OtherTerminals_AreNotSupported(string name, string value)
    {
        Assert.IsFalse(TerminalGraphics.DetectSixel(Env((name, value)), isWindows: false));
    }

    [TestMethod]
    public void DetectSixel_InsideTmux_IsNotSupported_UnlessForced()
    {
        Assert.IsFalse(TerminalGraphics.DetectSixel(Env(("TMUX", "/tmp/tmux-1000/default,1,0"), ("TERM", "foot")), isWindows: false));
        Assert.IsTrue(TerminalGraphics.DetectSixel(Env(("TMUX", "x"), ("ITERMINAL_SIXEL", "1")), isWindows: false));
    }

    [TestMethod]
    public void DetectSixel_TheOverride_WinsBothWays()
    {
        Assert.IsFalse(TerminalGraphics.DetectSixel(Env(("WT_SESSION", "abc"), ("ITERMINAL_SIXEL", "0")), isWindows: true));
        Assert.IsTrue(TerminalGraphics.DetectSixel(Env(("TERM", "xterm-256color"), ("ITERMINAL_SIXEL", "on")), isWindows: false));
    }

    [TestMethod]
    [DataRow("9x18", true, 9, 18)]
    [DataRow("10X20", true, 10, 20)]
    [DataRow("0x16", false, 0, 0)]
    [DataRow("abc", false, 0, 0)]
    [DataRow("", false, 0, 0)]
    public void TryParseCellSize_ReadsWidthAndHeight(string text, bool ok, int width, int height)
    {
        Assert.AreEqual(ok, TerminalGraphics.TryParseCellSize(text, out int w, out int h));
        if (ok)
        {
            Assert.AreEqual(width, w);
            Assert.AreEqual(height, h);
        }
    }

    [TestMethod]
    public void SetCellSize_ReplacesTheEstimate()
    {
        TerminalGraphics graphics = new(true);
        Assert.IsFalse(graphics.IsCellSizeKnown);

        graphics.SetCellSize(11, 23);

        Assert.AreEqual(11, graphics.CellWidth);
        Assert.AreEqual(23, graphics.CellHeight);
        Assert.IsTrue(graphics.IsCellSizeKnown);
    }

    [TestMethod]
    public void SetCellSize_IgnoresNonsense()
    {
        TerminalGraphics graphics = new(true);

        graphics.SetCellSize(0, 0);
        graphics.SetCellSize(5000, 5000);

        Assert.AreEqual(TerminalGraphics.DefaultCellWidth, graphics.CellWidth);
        Assert.IsFalse(graphics.IsCellSizeKnown);
    }

    [TestMethod]
    public void Fit_KeepsTheAspectRatioInPixels_AndStaysInsideTheRoom()
    {
        TerminalGraphics graphics = new(true, 10, 20, true);

        SixelFit fit = graphics.Fit(2.0, 50, 20);

        // Room: 500 × 400 pixels; a 2:1 picture is 500 × 250.
        Assert.AreEqual(500, fit.PixelWidth);
        Assert.AreEqual(250, fit.PixelHeight);
        Assert.AreEqual(50, fit.Columns);
        Assert.AreEqual(13, fit.Rows);
    }

    [TestMethod]
    public void Fit_ATallPicture_IsLimitedByTheHeight()
    {
        TerminalGraphics graphics = new(true, 10, 20, true);

        SixelFit fit = graphics.Fit(0.5, 50, 20);

        Assert.AreEqual(400, fit.PixelHeight);
        Assert.AreEqual(200, fit.PixelWidth);
        Assert.AreEqual(20, fit.Rows);
        Assert.AreEqual(20, fit.Columns);
    }

    [TestMethod]
    public void Fit_ARoomLargerThanTheLimit_ScalesTheBitmapDown()
    {
        TerminalGraphics graphics = new(true, 20, 40, true);

        SixelFit fit = graphics.Fit(1.0, 300, 100);

        Assert.IsTrue(fit.PixelWidth * fit.PixelHeight <= TerminalGraphics.MaxPixels);
        Assert.IsTrue(fit.Columns <= 300 && fit.Rows <= 100);
    }

    [TestMethod]
    public void Fit_NoRoom_IsEmpty()
    {
        Assert.AreEqual(new SixelFit(0, 0, 0, 0), new TerminalGraphics(true).Fit(1.0, 0, 10));
    }
}
