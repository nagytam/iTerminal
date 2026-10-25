using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class GridTest
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public void FixedStarAndSpanningTracks_SizeTheChildren()
    {
        var grid = new Grid(_theme) { ViewSize = new Size(40, 10) };
        grid.Columns.Clear();
        grid.Columns.AddRange([GridLength.Cells(10), GridLength.Star(), GridLength.Star(3)]);
        grid.Rows.Clear();
        grid.Rows.AddRange([GridLength.Cells(2), GridLength.Star()]);

        var first = new Control(_theme);
        var wide = new Control(_theme);
        var last = new Control(_theme);
        grid.Place(first, 0, 0);
        grid.Place(wide, 0, 1, columnSpan: 2);
        grid.Place(last, 1, 2);

        // 30 columns are left after the fixed one, split 1:3 => 7 and 23. Eight rows are left after the fixed row.
        Assert.AreEqual(new Position(0, 0), first.Position);
        Assert.AreEqual(new Size(10, 2), first.ViewSize);
        Assert.AreEqual(new Position(10, 0), wide.Position);
        Assert.AreEqual(new Size(30, 2), wide.ViewSize);
        Assert.AreEqual(new Position(17, 2), last.Position);
        Assert.AreEqual(new Size(23, 8), last.ViewSize);
    }

    [TestMethod]
    public void Auto_TakesTheWidestChildInTheTrack()
    {
        var grid = new Grid(_theme) { ViewSize = new Size(30, 3) };
        grid.Columns.Clear();
        grid.Columns.AddRange([GridLength.Auto, GridLength.Star()]);
        grid.Rows.Clear();
        grid.Rows.Add(GridLength.Star());

        var narrow = new Control(_theme) { ViewSize = new Size(4, 1) };
        var wider = new Control(_theme) { ViewSize = new Size(9, 1) };
        var rest = new Control(_theme);
        grid.Place(narrow, 0, 0);
        grid.Place(wider, 0, 0);
        grid.Place(rest, 0, 1);

        Assert.AreEqual(9, rest.Position.X);
        Assert.AreEqual(21, rest.ViewSize.Width);
    }

    [TestMethod]
    public void WithNoTracks_OneStarTrackFillsTheGrid()
    {
        var grid = new Grid(_theme) { ViewSize = new Size(20, 4) };
        grid.Columns.Clear();
        grid.Rows.Clear();
        var child = new Control(_theme);

        grid.Place(child, 0, 0);

        Assert.AreEqual(new Size(20, 4), child.ViewSize);
    }

    [TestMethod]
    public void Draw_RearrangesWhenTheGridIsResized()
    {
        var grid = new Grid(_theme) { ViewSize = new Size(20, 4) };
        var child = new Control(_theme);
        grid.Place(child, 0, 0);

        grid.ViewSize = new Size(50, 6);
        grid.Draw(new ScreenBuffer(60, 10));

        Assert.AreEqual(new Size(50, 6), child.ViewSize);
    }

    [TestMethod]
    public void Place_RejectsACellOutsideTheGrid()
    {
        var grid = new Grid(_theme);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => grid.Place(new Control(_theme), -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => grid.Place(new Control(_theme), 0, 0, columnSpan: 0));
    }
}
