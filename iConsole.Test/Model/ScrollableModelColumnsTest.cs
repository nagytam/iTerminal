using iConsole.Commands;
using iConsole.Data;
using iConsole.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iConsole.Test.Model;

/// <summary>
/// Scrolling a list laid out in several columns, row-major. A line is <c>ColumnCount</c> entries; the scroll
/// position is always a line start. The one-column numbers are the ones the list has always produced.
/// </summary>
[TestClass]
public class ScrollableModelColumnsTest
{
    private static ScrollableModel Create(int columns, long rows, long entries, long cursor, long scroll)
    {
        var model = new ScrollableModel
        {
            ColumnCount = columns,
            ViewSize = new Size(10, rows),
            ScrollableSize = new Size(1, entries),
        };
        model.CursorPosition = new Position(0, cursor);
        model.ScrollPosition = new Position(0, scroll);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollToVisible_CursorBelowTheView_ScrollsByWholeLines()
    {
        // 3 columns, 2 visible rows; cursor 6 is line 2, so the view must start at line 1 (entry 3).
        var model = Create(columns: 3, rows: 2, entries: 10, cursor: 6, scroll: 0);

        model.ScrollToVisible();

        Assert.AreEqual(3, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollToVisible_CursorAboveTheView_ScrollsToItsLine()
    {
        var model = Create(columns: 3, rows: 2, entries: 10, cursor: 2, scroll: 3);

        model.ScrollToVisible();

        Assert.AreEqual(0, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollDown_MovesOneRowDownTheSameColumn_AndScrollsWhenTheLineLeavesTheView()
    {
        var model = Create(columns: 3, rows: 2, entries: 10, cursor: 5, scroll: 0);

        new ScrollDownCommand(model).ExecuteAsync(null).Wait();

        Assert.AreEqual(8, model.CursorPosition.Y);
        Assert.AreEqual(3, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollDown_CursorStaysInTheVisibleLines_DoesNotScroll()
    {
        var model = Create(columns: 3, rows: 2, entries: 10, cursor: 2, scroll: 0);

        new ScrollDownCommand(model).ExecuteAsync(null).Wait();

        Assert.AreEqual(5, model.CursorPosition.Y);
        Assert.AreEqual(0, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AlignScroll_RoundsDownToALineStart_AndLeavesOneColumnAlone()
    {
        var columns = Create(columns: 3, rows: 2, entries: 10, cursor: 0, scroll: 4);
        columns.AlignScroll();
        Assert.AreEqual(3, columns.ScrollPosition.Y);

        var single = Create(columns: 1, rows: 2, entries: 10, cursor: 0, scroll: 4);
        single.AlignScroll();
        Assert.AreEqual(4, single.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColumnCount_BelowOne_ClampsToOne_AndSameValueRaisesNoChange()
    {
        var model = new ScrollableModel();
        var changes = 0;
        model.PropertyChanged += (_, _) => changes++;

        model.ColumnCount = 0;
        Assert.AreEqual(1, model.ColumnCount);
        Assert.AreEqual(0, changes);

        model.ColumnCount = 3;
        model.ColumnCount = 3;
        Assert.AreEqual(1, changes, "setting the same column count again must not raise PropertyChanged");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PageDown_MovesCursorAndScrollByAPage_ThenClampsAtTheEnd()
    {
        // Page = 2 visible rows x 3 columns = 6 entries.
        var model = Create(columns: 3, rows: 2, entries: 20, cursor: 0, scroll: 0);

        new ScrollPageDownCommand(model).ExecuteAsync(null).Wait();
        Assert.AreEqual(6, model.CursorPosition.Y);
        Assert.AreEqual(6, model.ScrollPosition.Y);

        new ScrollPageDownCommand(model).ExecuteAsync(null).Wait();
        Assert.AreEqual(12, model.CursorPosition.Y);
        Assert.AreEqual(12, model.ScrollPosition.Y);

        new ScrollPageUpCommand(model).ExecuteAsync(null).Wait();
        Assert.AreEqual(6, model.CursorPosition.Y);
        Assert.AreEqual(6, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PageDown_NearTheEnd_ClampsCursorAndScroll()
    {
        // 10 entries: the last full view starts at scroll 6 (lines 2-3 hold entries 6-9).
        var model = Create(columns: 3, rows: 2, entries: 10, cursor: 6, scroll: 6);

        new ScrollPageDownCommand(model).ExecuteAsync(null).Wait();

        Assert.AreEqual(9, model.CursorPosition.Y);
        Assert.AreEqual(6, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void End_InColumns_PutsTheLastLineAtTheBottom()
    {
        var model = Create(columns: 3, rows: 2, entries: 10, cursor: 0, scroll: 0);

        new ScrollToEndVerticalCommand(model).ExecuteAsync(null).Wait();

        Assert.AreEqual(9, model.CursorPosition.Y);
        Assert.AreEqual(6, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PageDown_OneColumn_MovesByRowsLikeBefore()
    {
        // Guard: the one-column paging numbers that existed before the columns did.
        var model = Create(columns: 1, rows: 10, entries: 100, cursor: 0, scroll: 0);

        new ScrollPageDownCommand(model).ExecuteAsync(null).Wait();

        Assert.AreEqual(10, model.CursorPosition.Y);
        Assert.AreEqual(10, model.ScrollPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PageUp_OneColumn_MovesByRowsLikeBefore()
    {
        // Guard: from cursor 19, scroll 10, a page up puts the cursor on 9 and the view at the top.
        var model = Create(columns: 1, rows: 10, entries: 100, cursor: 19, scroll: 10);

        new ScrollPageUpCommand(model).ExecuteAsync(null).Wait();

        Assert.AreEqual(9, model.CursorPosition.Y);
        Assert.AreEqual(0, model.ScrollPosition.Y);
    }
}
