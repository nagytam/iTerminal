using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class BoxPanelTest
{
    private static IThemeService Theme()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return themeService.Object;
    }

    private static BoxPanel Panel(int width, int height) => new(Theme()) { ViewSize = new Size(width, height) };

    [Timeout(60_000)]
    [TestMethod]
    public void Thickness_ClampsNegativeValuesAndDeflatesAndInflates()
    {
        var thickness = new Thickness(-2, 1, 3, 0);

        Assert.AreEqual(0, thickness.Left);
        Assert.AreEqual(3, thickness.Horizontal);
        Assert.AreEqual(1, thickness.Vertical);
        Assert.AreEqual(new Size(7, 0), thickness.Deflate(new Size(10, 1)));
        Assert.AreEqual(new Size(0, 0), thickness.Deflate(new Size(2, 0)));
        Assert.AreEqual(new Size(13, 6), thickness.Inflate(new Size(10, 5)));
        Assert.AreEqual(new Thickness(2, 3), new Thickness(1, 1) + new Thickness(1, 2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContentArea_IsWhatMarginBorderAndPaddingLeave()
    {
        BoxPanel panel = Panel(20, 10);
        panel.Margin = new Thickness(1, 2);
        panel.Padding = new Thickness(2, 1);

        // Horizontal: 1 + 1 + 2 each side; vertical: 2 + 1 + 1 each side.
        Assert.AreEqual(new Position(4, 4), panel.ContentPosition);
        Assert.AreEqual(new Size(12, 2), panel.ContentSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoBorder_TakesNoCell()
    {
        BoxPanel panel = Panel(10, 4);
        panel.BorderStyle = BorderStyle.None;

        Assert.AreEqual(new Position(0, 0), panel.ContentPosition);
        Assert.AreEqual(new Size(10, 4), panel.ContentSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetContent_FillsTheContentAreaAndFollowsLaterChanges()
    {
        BoxPanel panel = Panel(20, 8);
        var content = new Label("x", Theme());
        panel.SetContent(content);
        Assert.AreEqual(new Size(18, 6), content.ViewSize);

        panel.Padding = new Thickness(1);

        Assert.AreEqual(new Position(2, 2), content.Position);
        Assert.AreEqual(new Size(16, 4), content.ViewSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_PaintsTheBorderInsideTheMarginAndLeavesTheMarginAlone()
    {
        BoxPanel panel = Panel(8, 5);
        panel.Margin = new Thickness(1);
        panel.BorderStyle = BorderStyle.Rounded;
        var screen = new ScreenBuffer(8, 5);

        panel.Draw(screen);

        Assert.IsFalse(screen.IsWritten(0, 0));
        Assert.IsFalse(screen.IsWritten(7, 4));
        Assert.AreEqual('╭', screen.CharAt(1, 1));
        Assert.AreEqual('╮', screen.CharAt(6, 1));
        Assert.AreEqual('╰', screen.CharAt(1, 3));
        Assert.AreEqual('╯', screen.CharAt(6, 3));
        Assert.AreEqual('│', screen.CharAt(1, 2));
        Assert.AreEqual('─', screen.CharAt(3, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(BorderStyle.Single, '┌')]
    [DataRow(BorderStyle.Double, '╔')]
    [DataRow(BorderStyle.Thick, '┏')]
    [DataRow(BorderStyle.Dashed, '┌')]
    public void Draw_UsesTheGlyphsOfTheStyle(BorderStyle style, char topLeft)
    {
        BoxPanel panel = Panel(6, 3);
        panel.BorderStyle = style;
        var screen = new ScreenBuffer(6, 3);

        panel.Draw(screen);

        Assert.AreEqual(topLeft, screen.CharAt(0, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(TextAlignment.Left, 2)]
    [DataRow(TextAlignment.Center, 8)]
    [DataRow(TextAlignment.Right, 14)]
    public void Draw_SetsTheTitleOnTheTopEdgeByAlignment(TextAlignment alignment, int expectedX)
    {
        BoxPanel panel = Panel(20, 3);
        panel.Title = "Hi";
        panel.TitleAlignment = alignment;
        var screen = new ScreenBuffer(20, 3);

        panel.Draw(screen);

        Assert.AreEqual(expectedX, screen.Line(0).IndexOf(" Hi ", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ClipsALongTitleBetweenTheCorners()
    {
        BoxPanel panel = Panel(10, 3);
        panel.Title = "A very long title";
        var screen = new ScreenBuffer(10, 3);

        panel.Draw(screen);

        string top = screen.Line(0);
        Assert.AreEqual('┌', top[0]);
        Assert.AreEqual('┐', top[9]);
        Assert.IsTrue(top.Contains("A very"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ClearsThePaddingOnASecondDraw()
    {
        BoxPanel panel = Panel(10, 5);
        panel.Padding = new Thickness(1);
        var screen = new ScreenBuffer(10, 5);
        panel.Draw(screen);
        panel.Draw(screen);

        Assert.AreEqual(' ', screen.CharAt(1, 1));
        Assert.AreEqual(' ', screen.CharAt(5, 2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WithAMarginLargerThanThePanel_DrawsNothingAndDoesNotThrow()
    {
        BoxPanel panel = Panel(4, 3);
        panel.Margin = new Thickness(5);
        var screen = new ScreenBuffer(4, 3);

        panel.Draw(screen);

        Assert.AreEqual(new Size(0, 0), panel.ContentSize);
        Assert.IsFalse(screen.IsWritten(0, 0));
    }
}
