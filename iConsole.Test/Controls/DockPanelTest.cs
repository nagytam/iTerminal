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
public class DockPanelTest
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

    private static DockPanel Create(int width, int height)
    {
        return new DockPanel(Theme()) { ViewSize = new Size(width, height), Position = new Position(0, 0) };
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuStatusTreeAndEditor_AreDockedAroundTheFill()
    {
        DockPanel dock = Create(8, 5);
        dock.Add(new Fill(Theme(), 'm'), DockSide.Top);
        dock.Add(new Fill(Theme(), 's'), DockSide.Bottom);
        dock.Add(new Fill(Theme(), 't'), DockSide.Left, SizeRule.Fixed(3));
        dock.Add(new Fill(Theme(), 'e'), DockSide.Fill);

        ScreenBuffer screen = ScreenBuffer.Render(dock, 8, 5);

        Assert.AreEqual("mmmmmmmm|ttteeeee|ttteeeee|ttteeeee|ssssssss", string.Join("|", screen.Lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Right_TakesTheRightEdge_AndEarlierChildrenWinTheCorners()
    {
        DockPanel dock = Create(6, 3);
        dock.Add(new Fill(Theme(), 'r'), DockSide.Right, SizeRule.Fixed(2));
        dock.Add(new Fill(Theme(), 'b'), DockSide.Bottom);
        dock.Add(new Fill(Theme(), 'f'), DockSide.Fill);

        Assert.AreEqual("ffffrr|ffffrr|bbbbrr", string.Join("|", ScreenBuffer.Render(dock, 6, 3).Lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Gap_SeparatesStripsFromWhatFollows()
    {
        DockPanel dock = Create(4, 5);
        dock.Gap = 1;
        dock.Add(new Fill(Theme(), 'a'), DockSide.Top);
        dock.Add(new Fill(Theme(), 'b'), DockSide.Bottom);
        dock.Add(new Fill(Theme(), 'c'), DockSide.Fill);

        Assert.AreEqual("aaaa||cccc||bbbb", string.Join("|", ScreenBuffer.Render(dock, 4, 5).Lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Percent_IsOfTheSpaceLeftAtThatPoint()
    {
        DockPanel dock = Create(10, 1);
        dock.Add(new Fill(Theme(), 'a'), DockSide.Left, SizeRule.Percent(50));
        dock.Add(new Fill(Theme(), 'b'), DockSide.Left, SizeRule.Percent(40));
        dock.Add(new Fill(Theme(), 'c'), DockSide.Fill);

        Assert.AreEqual("aaaaabbccc", ScreenBuffer.Render(dock, 10, 1).Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChildrenAddedAfterTheFill_GetNoSpace()
    {
        DockPanel dock = Create(4, 2);
        dock.Add(new Fill(Theme(), 'f'), DockSide.Fill);
        Fill late = new(Theme(), 'x');
        dock.Add(late, DockSide.Top);

        Assert.AreEqual("ffff|ffff", string.Join("|", ScreenBuffer.Render(dock, 4, 2).Lines));
        Assert.AreEqual(0u, late.ViewSize.Width);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HiddenChild_TakesNoSpace()
    {
        DockPanel dock = Create(4, 3);
        Fill top = new(Theme(), 't');
        dock.Add(top, DockSide.Top);
        dock.Add(new Fill(Theme(), 'f'), DockSide.Fill);
        top.IsVisible = false;

        Assert.AreEqual("ffff|ffff|ffff", string.Join("|", ScreenBuffer.Render(dock, 4, 3).Lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TooLargeStrip_IsHeldToTheSpaceLeft_AndResizeRelayouts()
    {
        DockPanel dock = Create(4, 2);
        dock.Add(new Fill(Theme(), 'a'), DockSide.Top, SizeRule.Fixed(9));
        dock.Add(new Fill(Theme(), 'f'), DockSide.Fill);
        ScreenBuffer screen = new(4, 4);
        dock.Draw(screen);
        Assert.AreEqual("aaaa", screen.Line(1));

        dock.ViewSize = new Size(4, 4);
        dock.Draw(screen);

        Assert.AreEqual("aaaa", screen.Line(3));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Remove_DropsTheChild_AndNegativeGapCountsAsZero()
    {
        DockPanel dock = Create(4, 2);
        dock.Gap = -2;
        Fill top = new(Theme(), 't');
        dock.Add(top, DockSide.Top);
        dock.Add(new Fill(Theme(), 'f'), DockSide.Fill);
        dock.Remove(top);

        Assert.AreEqual(0, dock.Gap);
        Assert.AreEqual(1, dock.Items.Count);
        Assert.AreEqual("ffff|ffff", string.Join("|", ScreenBuffer.Render(dock, 4, 2).Lines));
    }
}
