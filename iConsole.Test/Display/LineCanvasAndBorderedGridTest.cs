using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Layouts;
using iConsole.Test.Core;

namespace iConsole.Test.Display;

[TestClass]
public class LineCanvasAndBorderedGridTest
{
    private readonly TestThemeService _theme = new();

    private static string Row(LineCanvas canvas, int y) =>
        new(Enumerable.Range(0, canvas.Width).Select(x => canvas.GlyphAt(x, y)).ToArray());

    // ---- LineCanvas ----

    [TestMethod]
    public void ARectangle_HasCornersEdgesAndAnEmptyMiddle()
    {
        var canvas = new LineCanvas(6, 4);

        canvas.AddRectangle(0, 0, 6, 4);

        Assert.AreEqual("┌────┐", Row(canvas, 0));
        Assert.AreEqual("│    │", Row(canvas, 1));
        Assert.AreEqual("│    │", Row(canvas, 2));
        Assert.AreEqual("└────┘", Row(canvas, 3));
    }

    [TestMethod]
    public void TwoBoxesSharingAnEdge_JoinWhereTheEdgeMeetsTheirBorders()
    {
        var canvas = new LineCanvas(9, 4);

        canvas.AddRectangle(0, 0, 5, 4);
        canvas.AddRectangle(4, 0, 5, 4);

        Assert.AreEqual("┌───┬───┐", Row(canvas, 0));
        Assert.AreEqual("│   │   │", Row(canvas, 1));
        Assert.AreEqual("└───┴───┘", Row(canvas, 3));
    }

    [TestMethod]
    public void FourBoxesInAGrid_MeetInACross_AndTheOuterEdgesGetTees()
    {
        var canvas = new LineCanvas(9, 7);

        canvas.AddRectangle(0, 0, 5, 4);
        canvas.AddRectangle(4, 0, 5, 4);
        canvas.AddRectangle(0, 3, 5, 4);
        canvas.AddRectangle(4, 3, 5, 4);

        Assert.AreEqual("├───┼───┤", Row(canvas, 3));
        Assert.AreEqual("┌───┬───┐", Row(canvas, 0));
        Assert.AreEqual("└───┴───┘", Row(canvas, 6));
    }

    [TestMethod]
    public void LinesMayBeAddedInAnyOrder_AndOverlapFreely()
    {
        var first = new LineCanvas(5, 3);
        first.AddLine(0, 1, 5, LineOrientation.Horizontal);
        first.AddLine(2, 0, 3, LineOrientation.Vertical);
        var second = new LineCanvas(5, 3);
        second.AddLine(2, 0, 3, LineOrientation.Vertical);
        second.AddLine(0, 1, 5, LineOrientation.Horizontal);
        second.AddLine(0, 1, 5, LineOrientation.Horizontal);

        for (int y = 0; y < 3; y++)
        {
            Assert.AreEqual(Row(first, y), Row(second, y));
        }

        Assert.AreEqual('┼', first.GlyphAt(2, 1));
        Assert.AreEqual('─', first.GlyphAt(0, 1) == '╶' ? '─' : first.GlyphAt(1, 1));
    }

    [TestMethod]
    [DataRow(BorderStyle.Double, "╔═╗", "║ ║", "╚═╝")]
    [DataRow(BorderStyle.Thick, "┏━┓", "┃ ┃", "┗━┛")]
    [DataRow(BorderStyle.Rounded, "╭─╮", "│ │", "╰─╯")]
    public void EveryStyleHasItsOwnGlyphs(BorderStyle style, string top, string middle, string bottom)
    {
        var canvas = new LineCanvas(3, 3);

        canvas.AddRectangle(0, 0, 3, 3, style);

        Assert.AreEqual(top, Row(canvas, 0));
        Assert.AreEqual(middle, Row(canvas, 1));
        Assert.AreEqual(bottom, Row(canvas, 2));
    }

    [TestMethod]
    public void WhereStylesMeet_TheHeavierOneWins_AndRoundedNeedsAllRound()
    {
        var mixed = new LineCanvas(5, 3);
        mixed.AddLine(0, 1, 5, LineOrientation.Horizontal, BorderStyle.Single);
        mixed.AddLine(2, 0, 3, LineOrientation.Vertical, BorderStyle.Double);
        Assert.AreEqual('╬', mixed.GlyphAt(2, 1));

        var rounded = new LineCanvas(3, 3);
        rounded.AddRectangle(0, 0, 3, 3, BorderStyle.Rounded);
        rounded.AddLine(1, 0, 3, LineOrientation.Vertical, BorderStyle.Single);
        Assert.AreEqual('╭', rounded.GlyphAt(0, 0));
        Assert.AreEqual('┬', rounded.GlyphAt(1, 0), "a tee has no round form");
    }

    [TestMethod]
    public void OutsideTheCanvas_IsIgnored_AndNoStyleDrawsNothing()
    {
        var canvas = new LineCanvas(3, 3);

        canvas.AddLine(-5, 1, 20, LineOrientation.Horizontal);
        canvas.AddLine(0, 0, 3, LineOrientation.Vertical, BorderStyle.None);
        canvas.AddRectangle(0, 0, 1, 1);
        canvas.AddLine(1, 1, 0, LineOrientation.Horizontal);

        Assert.AreEqual("───", Row(canvas, 1));
        Assert.AreEqual("   ", Row(canvas, 0));
        Assert.AreEqual(' ', canvas.GlyphAt(99, 99));
        Assert.IsFalse(canvas.HasLine(-1, 0));
    }

    [TestMethod]
    public void ALineOfOneCell_IsDrawnStraightThrough()
    {
        var canvas = new LineCanvas(3, 3);

        canvas.AddLine(1, 1, 1, LineOrientation.Horizontal);
        canvas.AddLine(2, 2, 1, LineOrientation.Vertical);

        Assert.AreEqual('─', canvas.GlyphAt(1, 1));
        Assert.AreEqual('│', canvas.GlyphAt(2, 2));
    }

    // ---- BorderedGrid ----

    private BorderedGrid Grid(int width, int height, int columns, int rows)
    {
        var grid = new BorderedGrid(_theme) { ViewSize = new Size(width, height) };
        grid.SetColumns([.. Enumerable.Repeat(SizeRule.Fill(), columns)]);
        grid.SetRows([.. Enumerable.Repeat(SizeRule.Fill(), rows)]);
        return grid;
    }

    private static ScreenBuffer Draw(Control control)
    {
        var screen = new ScreenBuffer((int)control.ViewSize.Width, (int)control.ViewSize.Height);
        control.Draw(screen);
        control.Draw(screen);
        return screen;
    }

    [TestMethod]
    public void TwoColumns_ShareOneMiddleLineThatJoinsTheFrame()
    {
        BorderedGrid grid = Grid(11, 5, 2, 1);
        grid.Add(null, 0, 0);
        grid.Add(null, 0, 1);

        ScreenBuffer screen = Draw(grid);

        Assert.AreEqual("┌────┬────┐", screen.Line(0));
        Assert.AreEqual("│    │    │", screen.Line(1));
        Assert.AreEqual("└────┴────┘", screen.Line(4));
    }

    [TestMethod]
    public void AGridOfFourCells_HasACrossInTheMiddle()
    {
        BorderedGrid grid = Grid(11, 7, 2, 2);
        for (int row = 0; row < 2; row++)
        {
            for (int column = 0; column < 2; column++)
            {
                grid.Add(null, row, column);
            }
        }

        ScreenBuffer screen = Draw(grid);

        Assert.AreEqual("├────┼────┤", screen.Line(3));
        Assert.AreEqual('┬', screen.CharAt(5, 0));
        Assert.AreEqual('┴', screen.CharAt(5, 6));
    }

    [TestMethod]
    public void AWideCell_SpansTracksAndTheLineBetweenThemIsNotDrawnThroughIt()
    {
        BorderedGrid grid = Grid(11, 7, 2, 2);
        grid.Add(null, 0, 0, columnSpan: 2);
        grid.Add(null, 1, 0);
        grid.Add(null, 1, 1);

        ScreenBuffer screen = Draw(grid);

        Assert.AreEqual("┌─────────┐", screen.Line(0));
        Assert.AreEqual("│         │", screen.Line(1), "no divider inside the wide cell");
        Assert.AreEqual("├────┬────┤", screen.Line(3));
    }

    [TestMethod]
    public void Content_FillsTheInsideOfItsCell()
    {
        BorderedGrid grid = Grid(11, 7, 2, 2);
        var left = new Control(_theme);
        var right = new Control(_theme);
        grid.Add(left, 0, 0);
        grid.Add(right, 0, 1);
        grid.Add(null, 1, 0, columnSpan: 2);

        grid.OnInitializeLayout();

        Assert.AreEqual(new Position(1, 1), left.Position);
        Assert.AreEqual(new Size(4, 2), left.ViewSize);
        Assert.AreEqual(new Position(6, 1), right.Position);
        Assert.AreEqual(new Size(4, 2), right.ViewSize);
        Assert.AreEqual((0, 0, 5, 3), grid.FrameOf(left));
        Assert.IsNull(grid.FrameOf(new Control(_theme)));
    }

    [TestMethod]
    public void FixedAndFillTracks_ShareWhatTheLinesLeave()
    {
        var grid = new BorderedGrid(_theme) { ViewSize = new Size(20, 5) };
        grid.SetColumns(SizeRule.Fixed(6), SizeRule.Fill());
        var content = new Control(_theme);
        grid.Add(content, 0, 1);

        grid.OnInitializeLayout();

        // 20 wide, three lines, so 17 for the tracks: 6 for the first, 11 for the second.
        Assert.AreEqual(new Position(8, 1), content.Position);
        Assert.AreEqual(new Size(11, 3), content.ViewSize);
    }

    [TestMethod]
    public void ATitle_IsSetIntoTheCellsTopLine_AndClipped()
    {
        BorderedGrid grid = Grid(21, 5, 2, 1);
        grid.Add(null, 0, 0, title: "Files");
        grid.Add(null, 0, 1, title: "A title that is much too long");

        ScreenBuffer screen = Draw(grid);

        Assert.AreEqual("┌─ Files ─┬─ A tit ─┐".Length, screen.Line(0).Length);
        StringAssertContains(screen.Line(0), " Files ");
        Assert.AreEqual('┬', screen.CharAt(10, 0));
        Assert.AreEqual('┐', screen.CharAt(20, 0));
    }

    [TestMethod]
    public void StyleAppliesToEveryLine_AndRedrawFillsTheBounds()
    {
        BorderedGrid grid = Grid(11, 5, 2, 1);
        grid.BorderStyle = BorderStyle.Double;
        grid.Add(null, 0, 0);
        grid.Add(null, 0, 1);

        ScreenBuffer screen = Draw(grid);

        Assert.AreEqual("╔════╦════╗", screen.Line(0));
        screen.AssertFillsBounds(grid);
    }

    [TestMethod]
    public void TinyAndEmptyGrids_DoNotThrow()
    {
        foreach ((int w, int h) in new[] { (1, 1), (2, 2), (3, 1), (5, 3) })
        {
            BorderedGrid grid = Grid(w, h, 3, 3);
            grid.Add(new Control(_theme), 2, 2, 5, 5, "x");
            grid.Add(null, 0, 0);
            grid.OnInitializeLayout();
            Draw(grid);
        }

        var empty = new BorderedGrid(_theme) { ViewSize = new Size(10, 4) };
        Draw(empty);
    }

    private static void StringAssertContains(string actual, string expected) =>
        Assert.IsTrue(actual.Contains(expected, StringComparison.Ordinal), $"[{actual}] lacks [{expected}]");
}
