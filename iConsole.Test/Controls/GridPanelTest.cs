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
public class GridPanelTest
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

    private static GridPanel Create(int width, int height)
    {
        return new GridPanel(Theme()) { ViewSize = new Size(width, height), Position = new Position(0, 0) };
    }

    [TestMethod]
    public void Cells_AreDrawnAtTheirRowAndColumn()
    {
        GridPanel grid = Create(6, 4);
        grid.SetColumns(SizeRule.Fixed(2), SizeRule.Fill());
        grid.SetRows(SizeRule.Fixed(1), SizeRule.Fill());
        grid.Add(new Fill(Theme(), 'a'), 0, 0);
        grid.Add(new Fill(Theme(), 'b'), 0, 1);
        grid.Add(new Fill(Theme(), 'c'), 1, 0);
        grid.Add(new Fill(Theme(), 'd'), 1, 1);

        ScreenBuffer screen = ScreenBuffer.Render(grid, 6, 4);

        Assert.AreEqual("aabbbb|ccdddd|ccdddd|ccdddd", string.Join("|", screen.Lines));
    }

    [TestMethod]
    public void Span_CoversSeveralTracksAndTheGapsBetweenThem()
    {
        GridPanel grid = Create(8, 3);
        grid.ColumnGap = 1;
        grid.SetColumns(SizeRule.Fixed(2), SizeRule.Fixed(2), SizeRule.Fixed(2));
        grid.SetRows(SizeRule.Fixed(1), SizeRule.Fixed(1), SizeRule.Fixed(1));
        grid.Add(new Fill(Theme(), 'w'), 0, 0, columnSpan: 3);
        grid.Add(new Fill(Theme(), 't'), 1, 2, rowSpan: 2);

        ScreenBuffer screen = ScreenBuffer.Render(grid, 8, 3);

        Assert.AreEqual("wwwwwwww|      tt|      tt", string.Join("|", screen.Lines));
    }

    [TestMethod]
    public void Percent_AndFillTracks_ShareTheWidth()
    {
        GridPanel grid = Create(10, 1);
        grid.SetColumns(SizeRule.Percent(30), SizeRule.Fill(1), SizeRule.Fill(1));
        grid.Add(new Fill(Theme(), 'a'), 0, 0);
        grid.Add(new Fill(Theme(), 'b'), 0, 1);
        grid.Add(new Fill(Theme(), 'c'), 0, 2);

        Assert.AreEqual("aaabbbbccc", ScreenBuffer.Render(grid, 10, 1).Line(0));
    }

    [TestMethod]
    public void Resize_RedistributesOnTheNextDraw()
    {
        GridPanel grid = Create(10, 1);
        grid.SetColumns(SizeRule.Percent(50), SizeRule.Fill());
        grid.Add(new Fill(Theme(), 'a'), 0, 0);
        grid.Add(new Fill(Theme(), 'b'), 0, 1);
        ScreenBuffer screen = new(20, 1);
        grid.Draw(screen);
        Assert.AreEqual("aaaaabbbbb", screen.Text(0, 0, 10));

        grid.ViewSize = new Size(20, 1);
        grid.Draw(screen);

        Assert.AreEqual("aaaaaaaaaabbbbbbbbbb", screen.Line(0));
    }

    [TestMethod]
    public void OutOfRangePlacement_IsClampedToTheLastTrack()
    {
        GridPanel grid = Create(4, 2);
        grid.SetColumns(SizeRule.Fixed(2), SizeRule.Fixed(2));
        grid.SetRows(SizeRule.Fixed(2));
        grid.Add(new Fill(Theme(), 'x'), 9, 9, 9, 9);

        Assert.AreEqual("  xx|  xx", string.Join("|", ScreenBuffer.Render(grid, 4, 2).Lines));
    }

    [TestMethod]
    public void HiddenChild_LeavesItsCellsEmpty()
    {
        GridPanel grid = Create(4, 1);
        grid.SetColumns(SizeRule.Fixed(2), SizeRule.Fixed(2));
        Fill hidden = new(Theme(), 'a');
        grid.Add(hidden, 0, 0);
        grid.Add(new Fill(Theme(), 'b'), 0, 1);
        hidden.IsVisible = false;

        Assert.AreEqual("  bb", ScreenBuffer.Render(grid, 4, 1).Line(0));
    }

    [TestMethod]
    public void Remove_FreesTheCell_AndNegativeGapsCountAsZero()
    {
        GridPanel grid = Create(4, 1);
        grid.ColumnGap = -3;
        grid.RowGap = -3;
        grid.SetColumns(SizeRule.Fixed(2), SizeRule.Fixed(2));
        Fill first = new(Theme(), 'a');
        grid.Add(first, 0, 0);
        grid.Add(new Fill(Theme(), 'b'), 0, 1);
        grid.Remove(first);

        Assert.AreEqual(0, grid.ColumnGap);
        Assert.AreEqual(1, grid.Items.Count);
        Assert.AreEqual("  bb", ScreenBuffer.Render(grid, 4, 1).Line(0));
    }

    [TestMethod]
    public void EmptyTrackLists_FallBackToOneFillingTrack()
    {
        GridPanel grid = Create(3, 1);
        grid.SetColumns();
        grid.SetRows();
        grid.Add(new Fill(Theme(), 'a'), 0, 0);

        Assert.AreEqual("aaa", ScreenBuffer.Render(grid, 3, 1).Line(0));
    }
}
