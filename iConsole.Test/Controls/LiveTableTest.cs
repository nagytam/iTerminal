using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iConsole.Test.Controls;

[TestClass]
public class LiveTableTest
{
    private readonly TestThemeService _theme = new();
    private readonly VirtualTimeProvider _time = new();

    private LiveTable Create(int width = 40, int height = 6)
    {
        var table = new LiveTable(_theme, _time) { ViewSize = new Size(width, height) };
        table.AddColumn("Name", 10);
        table.AddColumn("State", 8);
        table.AddColumn("Done", 6, TextAlignment.Right);
        table.SetFocus();
        return table;
    }

    private static ScreenBuffer Draw(Control control, int width, int height)
    {
        var screen = new ScreenBuffer(width, height);
        control.Draw(screen);
        control.Draw(screen);
        return screen;
    }

    private static KeyEvent Key(ConsoleKey key) => new(' ', key, false, false, false);

    [Timeout(60_000)]
    [TestMethod]
    public void SetRow_AddsRowsInOrder_AndReplacesByKey()
    {
        LiveTable table = Create();

        table.SetRow("b", "beta", "run", "10");
        table.SetRow("a", "alpha", "run", "20");
        table.SetRow("b", "beta", "done", "100");

        CollectionAssert.AreEqual(new[] { "b", "a" }, table.Keys.ToArray());
        Assert.AreEqual("done", table.GetCell("b", 1));
        Assert.AreEqual(2, table.RowCount);
        Assert.IsNull(table.GetCell("nope", 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetCell_ChangesOneCell_AndIgnoresAColumnThatDoesNotExist()
    {
        LiveTable table = Create();
        table.SetRow("a", "alpha", "run", "1");

        table.SetCell("a", 2, "50");
        table.SetCell("a", 9, "x");
        table.SetCell("new", 0, "fresh");

        Assert.AreEqual("50", table.GetCell("a", 2));
        Assert.AreEqual("fresh", table.GetCell("new", 0));
        Assert.AreEqual(2, table.RowCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemoveRow_AndClear()
    {
        LiveTable table = Create();
        table.SetRow("a", "x", "y", "z");
        table.SetRow("b", "x", "y", "z");

        Assert.IsTrue(table.RemoveRow("a"));
        Assert.IsFalse(table.RemoveRow("a"));
        table.Clear();

        Assert.AreEqual(0, table.RowCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsHeaderAndRowsWithAlignment()
    {
        LiveTable table = Create();
        table.SetRow("a", "alpha", "run", "7");

        ScreenBuffer screen = Draw(table, 40, 6);

        StringAssert.Contains(screen.Line(0), "Name");
        StringAssert.Contains(screen.Line(0), "State");
        Assert.AreEqual("alpha     " + " " + "run     " + " " + "     7", screen.Line(1).TrimEnd());
        Assert.AreEqual(new ColorScheme().SelectedBackground, screen.BackgroundAt(0, 0), "header uses the selected colours");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_AChangedCellIsHighlightedUntilTheFlashEnds()
    {
        LiveTable table = Create();
        table.SetRow("a", "alpha", "run", "1");
        ColorScheme scheme = new();
        Draw(table, 40, 6);

        table.SetCell("a", 2, "2");
        ScreenBuffer flashing = Draw(table, 40, 6);
        Assert.AreEqual(scheme.SelectedBackground, flashing.BackgroundAt(22, 1), "the changed cell flashes");
        Assert.AreEqual(scheme.Background, flashing.BackgroundAt(0, 1), "the others do not");

        _time.Advance(TimeSpan.FromSeconds(1));
        ScreenBuffer faded = Draw(table, 40, 6);
        Assert.AreEqual(scheme.Background, faded.BackgroundAt(22, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFirstFill_AndAnUnchangedValue_DoNotFlash()
    {
        LiveTable table = Create();
        table.SetRow("a", "alpha", "run", "1");
        table.SetCell("a", 2, "1");

        ScreenBuffer screen = Draw(table, 40, 6);

        Assert.AreEqual(new ColorScheme().Background, screen.BackgroundAt(22, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FlashDurationZero_TurnsTheHighlightOff()
    {
        LiveTable table = Create();
        table.FlashDuration = TimeSpan.Zero;
        table.SetRow("a", "alpha", "run", "1");
        table.SetCell("a", 2, "2");

        Assert.AreEqual(new ColorScheme().Background, Draw(table, 40, 6).BackgroundAt(22, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetCell_ColourTintsTheText()
    {
        LiveTable table = Create();
        table.FlashDuration = TimeSpan.Zero;
        table.SetRow("a", "alpha", "run", "1");

        table.SetCell("a", 1, "FAIL", Color.Red);

        Assert.AreEqual(Color.Red, Draw(table, 40, 6).ForegroundAt(11, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SortColumn_OrdersNumbersAsNumbers_AndDescendingReverses()
    {
        LiveTable table = Create();
        table.SetRow("a", "a", "x", "10");
        table.SetRow("b", "b", "x", "9");
        table.SetRow("c", "c", "x", "100");

        table.SortColumn = 2;
        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, table.Keys.ToArray());

        table.SortDescending = true;
        CollectionAssert.AreEqual(new[] { "c", "a", "b" }, table.Keys.ToArray());

        table.SortColumn = -1;
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, table.Keys.ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CompareCells_NumbersBeforeText_AndPercentSigns()
    {
        Assert.IsTrue(LiveTable.CompareCells("9", "10") < 0);
        Assert.IsTrue(LiveTable.CompareCells("5%", "40%") < 0);
        Assert.IsTrue(LiveTable.CompareCells("1", "abc") < 0);
        Assert.IsTrue(LiveTable.CompareCells("Apple", "banana") < 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FittedColumns_TakeTheirContentWidth_AndShrinkWhenTheTableIsNarrow()
    {
        var columns = new[] { new LiveTableColumn("A"), new LiveTableColumn("B", 10), new LiveTableColumn("C") };

        int[] wide = LiveTable.ComputeWidths(columns, [12, 0, 4], 80);
        int[] narrow = LiveTable.ComputeWidths(columns, [30, 0, 30], 30);

        CollectionAssert.AreEqual(new[] { 12, 10, 4 }, wide);
        Assert.IsTrue(narrow.Sum() + 2 <= 30);
        Assert.AreEqual(10, narrow[1], "a fixed width is kept while a fitted column can still shrink");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LongText_IsCutWithAnEllipsis()
    {
        LiveTable table = Create();
        table.SetRow("a", "a very long name indeed", "run", "1");

        StringAssert.Contains(Draw(table, 40, 6).Line(1), "a very lo…");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Scrolling_KeysMoveTheWindow_AndKeepNewestVisibleFollowsTheEnd()
    {
        LiveTable table = Create(40, 4);
        for (int i = 0; i < 10; i++)
        {
            table.SetRow($"k{i}", $"row{i}", "x", "1");
        }

        StringAssert.Contains(Draw(table, 40, 4).Line(1), "row0");
        await table.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        StringAssert.Contains(Draw(table, 40, 4).Line(1), "row1");
        await table.HandleKeyEventAsync(Key(ConsoleKey.End));
        StringAssert.Contains(Draw(table, 40, 4).Line(3), "row9");
        await table.HandleKeyEventAsync(Key(ConsoleKey.Home));
        StringAssert.Contains(Draw(table, 40, 4).Line(1), "row0");

        table.KeepNewestVisible = true;
        table.SetRow("k10", "row10", "x", "1");
        StringAssert.Contains(Draw(table, 40, 4).Line(3), "row10");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EmptyTables_SayWhatIsMissing()
    {
        var bare = new LiveTable(_theme, _time) { ViewSize = new Size(30, 4) };
        Assert.IsTrue(Draw(bare, 30, 4).Contains("No rows"));

        LiveTable table = Create();
        ScreenBuffer screen = Draw(table, 40, 6);
        StringAssert.Contains(screen.Line(0), "Name");
        StringAssert.Contains(screen.Line(1), "No rows");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnIdle_RepaintsOnAChangeAndWhileAHighlightFades()
    {
        LiveTable table = Create();
        table.SetRow("a", "alpha", "run", "1");
        Draw(table, 40, 6);
        table.HasStateChanged = false;
        table.OnIdle();
        Assert.IsFalse(table.HasStateChanged);

        table.SetCell("a", 2, "2");
        table.OnIdle();
        Assert.IsTrue(table.HasStateChanged);

        Draw(table, 40, 6);
        table.HasStateChanged = false;
        _time.Advance(TimeSpan.FromMilliseconds(300));
        table.OnIdle();
        Assert.IsTrue(table.HasStateChanged, "the highlight is still fading");

        _time.Advance(TimeSpan.FromSeconds(5));
        Draw(table, 40, 6);
        table.HasStateChanged = false;
        _time.Advance(TimeSpan.FromSeconds(5));
        table.OnIdle();
        Assert.IsFalse(table.HasStateChanged, "once faded, time alone repaints nothing");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UpdatesFromManyThreads_AreAllKept()
    {
        LiveTable table = Create();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 100; i++)
            {
                table.SetRow($"{t}-{i}", "n", "s", i.ToString());
            }
        })));

        Assert.AreEqual(800, table.RowCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TinySizes_DoNotThrow_AndTheTableFillsItsBounds()
    {
        LiveTable table = Create();
        table.SetRow("a", "alpha", "run", "1");
        foreach ((int w, int h) in new[] { (40, 6), (12, 3), (5, 2), (1, 1), (3, 1) })
        {
            table.ViewSize = new Size(w, h);
            Draw(table, w, h);
        }

        table.ViewSize = new Size(40, 6);
        Draw(table, 40, 6).AssertFillsBounds(table);
    }
}
