using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Test.Controls;

[TestClass]
public class StackPanelTest
{
    private sealed class Fill(IThemeService theme, char c) : Control(theme)
    {
        public override void Draw(IBuffer buffer)
        {
            for (uint y = 0; y < ViewSize.Height; y++)
            {
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), new Size(ViewSize.Width, 1),
                    new ConxelString(new string(c, (int)ViewSize.Width), Color.White, Color.Black));
            }
        }
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static StackPanel Create(int width, int height, Orientation orientation)
    {
        return new StackPanel(Theme()) { Orientation = orientation, ViewSize = new Size(width, height), Position = new Position(0, 0) };
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Row_FixedPercentAndFill_AreDrawnSideBySide()
    {
        StackPanel panel = Create(10, 2, Orientation.Horizontal);
        panel.Add(new Fill(Theme(), 'a'), SizeRule.Fixed(2));
        panel.Add(new Fill(Theme(), 'b'), SizeRule.Percent(30));
        panel.Add(new Fill(Theme(), 'c'));

        ScreenBuffer screen = ScreenBuffer.Render(panel, 10, 2);

        Assert.AreEqual("aabbbccccc", screen.Line(0));
        Assert.AreEqual("aabbbccccc", screen.Line(1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Column_WithGap_LeavesBlankRowsBetweenChildren()
    {
        StackPanel panel = Create(3, 7, Orientation.Vertical);
        panel.Gap = 1;
        panel.Add(new Fill(Theme(), 'a'), SizeRule.Fixed(1));
        panel.Add(new Fill(Theme(), 'b'), SizeRule.Fill(1));
        panel.Add(new Fill(Theme(), 'c'), SizeRule.Fill(1));

        ScreenBuffer screen = ScreenBuffer.Render(panel, 3, 7);

        Assert.AreEqual("aaa||bbb|bbb||ccc|ccc", string.Join("|", screen.Lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Resize_RedistributesOnTheNextDraw()
    {
        StackPanel panel = Create(10, 1, Orientation.Horizontal);
        panel.Add(new Fill(Theme(), 'a'), SizeRule.Percent(50));
        panel.Add(new Fill(Theme(), 'b'));
        ScreenBuffer screen = new(20, 1);
        panel.Draw(screen);
        Assert.AreEqual("aaaaabbbbb", screen.Text(0, 0, 10));

        panel.ViewSize = new Size(20, 1);
        panel.Draw(screen);

        Assert.AreEqual("aaaaaaaaaabbbbbbbbbb", screen.Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HiddenChild_TakesNoSpaceAndNoGap()
    {
        StackPanel panel = Create(9, 1, Orientation.Horizontal);
        panel.Gap = 1;
        Fill hidden = new(Theme(), 'x');
        panel.Add(new Fill(Theme(), 'a'));
        panel.Add(hidden);
        panel.Add(new Fill(Theme(), 'b'));
        hidden.IsVisible = false;

        ScreenBuffer screen = ScreenBuffer.Render(panel, 9, 1);

        Assert.AreEqual("aaaa bbbb", screen.Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetRule_And_Remove_ReLayOut()
    {
        StackPanel panel = Create(10, 1, Orientation.Horizontal);
        Fill first = new(Theme(), 'a');
        Fill second = new(Theme(), 'b');
        panel.Add(first, SizeRule.Fixed(2));
        panel.Add(second);

        panel.SetRule(first, SizeRule.Fixed(6));
        Assert.AreEqual("aaaaaabbbb", ScreenBuffer.Render(panel, 10, 1).Line(0));

        panel.Remove(first);
        Assert.AreEqual("bbbbbbbbbb", ScreenBuffer.Render(panel, 10, 1).Line(0));
        Assert.AreEqual(1, panel.Items.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChildSpansTheCrossAxis()
    {
        StackPanel panel = Create(8, 3, Orientation.Horizontal);
        Fill child = new(Theme(), 'a');
        panel.Add(child);

        ScreenBuffer.Render(panel, 8, 3);

        Assert.AreEqual(8u, (uint)child.ViewSize.Width);
        Assert.AreEqual(3u, (uint)child.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NegativeGap_IsClampedToZero()
    {
        StackPanel panel = Create(4, 1, Orientation.Horizontal);
        panel.Gap = -3;

        Assert.AreEqual(0, panel.Gap);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_EmptySizeOrNoChildren_DoesNotThrow()
    {
        new StackPanel(Theme()) { ViewSize = new Size(0, 0) }.Draw(new ScreenBuffer(5, 5));
        Create(5, 5, Orientation.Vertical).Draw(new ScreenBuffer(5, 5));
    }
}
