using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Viewers;

/// <summary>The search hits are marked on the vertical scrollbar, one dot per line, except where the thumb covers one.</summary>
[TestClass]
public class ScrollBarMarkersTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextViewerControl NewViewer(int width, int height, int lineCount)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
        TextViewerControl viewer = new(false, Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
        root.AddControl(viewer);
        for (int i = 0; i < lineCount; i++)
        {
            viewer.TextModel.Lines.Add("line " + i);
        }

        // The scrollbar gets its model from the viewer on initialisation, as in the running app.
        viewer.OnInitializeAsync().GetAwaiter().GetResult();
        viewer.OnInitializeLayout();
        viewer.Refresh();
        return viewer;
    }

    private static ScreenBuffer Draw(TextViewerControl viewer)
    {
        var screen = new ScreenBuffer((int)viewer.ViewSize.Width, (int)viewer.ViewSize.Height);
        viewer.Draw(screen);
        viewer.Draw(screen);
        return screen;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AHitNearTheEnd_IsMarkedOnTheBottomOfTheTrack()
    {
        // Ten rows: the track is rows 1 to 7 (the bar is one row short of the viewer), and a hit on the last line sits on row 7, clear of the thumb at the top.
        TextViewerControl viewer = NewViewer(20, 10, 100);
        viewer.SearchModel.Hits.Add(new Position(0, 99));

        ScreenBuffer screen = Draw(viewer);

        Assert.AreEqual(iConsole.Controls.ScrollBar.ScrollBar.MarkCharacter, screen.CharAt(19, 7));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AHitUnderTheThumb_IsNotDrawnOverIt()
    {
        // Cursor on the first line: the thumb is on row 1, and a hit on the first line is drawn there, so the thumb stays.
        TextViewerControl viewer = NewViewer(20, 10, 100);
        viewer.SearchModel.Hits.Add(new Position(0, 0));

        ScreenBuffer screen = Draw(viewer);

        Assert.AreNotEqual(iConsole.Controls.ScrollBar.ScrollBar.MarkCharacter, screen.CharAt(19, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoHits_NoMarks()
    {
        TextViewerControl viewer = NewViewer(20, 10, 100);
        ScreenBuffer screen = Draw(viewer);

        for (int y = 0; y < 10; y++)
        {
            Assert.AreNotEqual(iConsole.Controls.ScrollBar.ScrollBar.MarkCharacter, screen.CharAt(19, y));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ClearingTheHits_RemovesTheMarks()
    {
        TextViewerControl viewer = NewViewer(20, 10, 100);
        viewer.SearchModel.Hits.Add(new Position(0, 99));
        Assert.AreEqual(iConsole.Controls.ScrollBar.ScrollBar.MarkCharacter, Draw(viewer).CharAt(19, 7));

        viewer.SearchModel.Hits.Clear();

        Assert.AreNotEqual(iConsole.Controls.ScrollBar.ScrollBar.MarkCharacter, Draw(viewer).CharAt(19, 7));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarksAreKeptToTheLinesOfTheHits_NotTheHitColumns()
    {
        // Two hits on the same last line are one dot, and the text is untouched.
        TextViewerControl viewer = NewViewer(20, 10, 100);
        viewer.SearchModel.Hits.Add(new Position(0, 99));
        viewer.SearchModel.Hits.Add(new Position(5, 99));

        ScreenBuffer screen = Draw(viewer);

        Assert.AreEqual(iConsole.Controls.ScrollBar.ScrollBar.MarkCharacter, screen.CharAt(19, 7));
        Assert.AreEqual("line 99", viewer.TextModel.Lines[99]);
    }
}
