using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Model;
using iConsole.Display;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Controls.ScrollBar;

/// <summary>A press on the track of a scroll bar starts a drag that moves the cursor with the pointer; the arrows never start one.</summary>
[TestClass]
public class ScrollBarDragTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void CursorForDrag_MapsTheTrackProportionallyAndClampsTheArrowCells()
    {
        // A bar 10 cells long: cell 0 and cell 9 are the arrows, cells 1..8 are the track.
        Assert.AreEqual(0, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(1, 10, 100, 50));
        Assert.AreEqual(99, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(8, 10, 100, 50));
        Assert.AreEqual(57, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(5, 10, 100, 50));
        Assert.AreEqual(0, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(0, 10, 100, 50), "an arrow cell clamps to the first position");
        Assert.AreEqual(99, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(9, 10, 100, 50), "an arrow cell clamps to the last position");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CursorForDrag_KeepsTheCurrentCursorWhenThereIsNothingToScroll()
    {
        Assert.AreEqual(7, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(5, 10, 1, 7));
        Assert.AreEqual(7, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(5, 3, 100, 7), "a bar too short for a track");
        Assert.AreEqual(7, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(5, 10, 0, 7));
    }

    private static (global::iConsole.Controls.ScrollBar.ScrollBar Bar, Mock<IScrollableModel> Model) NewVerticalBar(long cursorY)
    {
        Mock<IThemeService> theme = new();
        theme.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        Mock<IScrollableModel> model = new();
        model.Setup(m => m.CursorPosition).Returns(new Position(0, cursorY));
        model.Setup(m => m.ScrollableSize).Returns(new Size(1, 100));
        model.Setup(m => m.ViewSize).Returns(new Size(1, 10));
        model.Setup(m => m.ScrollPosition).Returns(new Position(0, 0));
        var bar = new global::iConsole.Controls.ScrollBar.ScrollBar(theme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10),
            ScrollBarOrientation = Orientation.Vertical,
            ScrollableModel = model.Object,
        };
        return (bar, model);
    }

    private static MouseEvent Press(long row) => new() { Position = new Position(0, row), LeftClicked = true };

    private static MouseEvent Drag(long row) => new() { Position = new Position(0, row), IsDrag = true };

    [Timeout(60_000)]
    [TestMethod]
    public async Task APressOnTheTrackDragsTheCursorUntilTheReleaseAndAnArrowPressDoesNotDrag()
    {
        (global::iConsole.Controls.ScrollBar.ScrollBar bar, Mock<IScrollableModel> model) = NewVerticalBar(0);

        await bar.HandleMouseEventAsync(Press(5));
        Assert.AreEqual(57, model.Object.CursorPosition.Y, "the press jumps to the matching position, as a click always has");

        await bar.HandleMouseEventAsync(Drag(3));
        Assert.AreEqual(CursorForDragRow(3), model.Object.CursorPosition.Y);

        await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 3), IsRelease = true });
        await bar.HandleMouseEventAsync(Drag(8));
        Assert.AreEqual(CursorForDragRow(3), model.Object.CursorPosition.Y, "after the release the pointer moves nothing");

        (global::iConsole.Controls.ScrollBar.ScrollBar arrowBar, Mock<IScrollableModel> arrowModel) = NewVerticalBar(50);
        await arrowBar.HandleMouseEventAsync(Press(0));
        Assert.AreEqual(49, arrowModel.Object.CursorPosition.Y, "the up arrow steps one cell");
        await arrowBar.HandleMouseEventAsync(Drag(8));
        Assert.AreEqual(49, arrowModel.Object.CursorPosition.Y, "an arrow press starts no drag");
    }

    private static long CursorForDragRow(long row) =>
        global::iConsole.Controls.ScrollBar.ScrollBar.CursorForDrag(row, 10, 100, 0);
}
