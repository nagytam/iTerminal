using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

[TestClass]
public class CsvGridTest
{
    private static readonly string[] Headers = ["name"];
    private static readonly string[][] Rows = [["A"], ["B"], ["C"]];

    private static CsvGrid CreateFocusedGrid()
    {
        var grid = new CsvGrid(new ThemeService()) { ViewSize = new Size(30, 8) };
        grid.SetData(Headers, Rows);
        grid.SetFocus();
        return grid;
    }

    private static Task<bool> PressSpace(CsvGrid grid) => grid.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));

    private static Task<bool> PressUp(CsvGrid grid) => grid.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false));

    [TestMethod]
    public async Task SpaceMarksTheCursorRowAndMovesDown()
    {
        CsvGrid grid = CreateFocusedGrid();

        Assert.IsTrue(await PressSpace(grid));

        CollectionAssert.AreEqual(new[] { 0 }, grid.MarkedRows.ToArray());
        Assert.AreEqual(1, grid.CursorRow);
    }

    [TestMethod]
    public async Task SpaceOnAMarkedRowUnmarksIt()
    {
        CsvGrid grid = CreateFocusedGrid();
        await PressSpace(grid);
        await PressUp(grid);

        await PressSpace(grid);

        Assert.AreEqual(0, grid.MarkedCount);
    }

    [TestMethod]
    public async Task SpaceDoesNothingWhenTheGridIsNotFocused()
    {
        var grid = new CsvGrid(new ThemeService()) { ViewSize = new Size(30, 8) };
        grid.SetData(Headers, Rows);

        Assert.IsFalse(await PressSpace(grid));

        Assert.AreEqual(0, grid.MarkedCount);
    }

    [TestMethod]
    public async Task SettingNewRowsClearsTheMarks()
    {
        CsvGrid grid = CreateFocusedGrid();
        await PressSpace(grid);

        grid.SetData(Headers, Rows);

        Assert.AreEqual(0, grid.MarkedCount);
    }

    [TestMethod]
    public async Task AMarkedRowShowsTheMarkOnScreenAndAnUnmarkedRowDoesNot()
    {
        CsvGrid grid = CreateFocusedGrid();
        await PressSpace(grid);

        // Drawn twice: a control paints its whole rectangle each time, so a second draw must show the same thing.
        string first = ScreenBuffer.Render(grid, 30, 8).ToString();
        string second = ScreenBuffer.Render(grid, 30, 8).ToString();

        Assert.AreEqual(first, second);
        Assert.IsTrue(first.Contains("* A", StringComparison.Ordinal), first);
        Assert.IsFalse(first.Contains("* B", StringComparison.Ordinal), first);
        Assert.IsFalse(first.Contains("* C", StringComparison.Ordinal), first);
    }
}
