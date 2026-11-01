using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Test.Core;

namespace iConsole.Test.Controls;

/// <summary>
/// Wrapped text: the reading position survives a change of width, and in the editor the caret follows the wrapped rows and
/// Up and Down move by row.
/// </summary>
[TestClass]
public class WrappedReflowAndCaretTest
{
    private readonly TestThemeService _theme = new();

    private TextViewerControl Viewer(IEnumerable<string> lines, int width, int height)
    {
        var root = new Control(_theme) { ViewSize = new Size(120, 40) };
        var viewer = new TextViewerControl(false, _theme) { Position = new Position(0, 0), ViewSize = new Size(width, height), WrapLines = true };
        root.AddControl(viewer);
        foreach (var line in lines)
        {
            viewer.TextModel.Lines.Add(line);
        }
        viewer.OnInitializeLayout();
        return viewer;
    }

    private TextEditorControl Editor(IEnumerable<string> lines, int width, int height)
    {
        var root = new Control(_theme) { ViewSize = new Size(120, 40) };
        var editor = new TextEditorControl(_theme) { Position = new Position(0, 0), ViewSize = new Size(width, height), WrapLines = true };
        root.AddControl(editor);
        foreach (var line in lines)
        {
            editor.TextModel.Lines.Add(line);
        }
        editor.OnInitializeLayout();
        editor.Refresh();
        editor.SetFocus();
        return editor;
    }

    /// <summary>Ten lines, each 60 characters: "NN" then a run of x. The number says which line a row came from.</summary>
    private static string[] TenLines() => Enumerable.Range(0, 10).Select(i => $"{i:D2} " + new string('x', 57)).ToArray();

    [Timeout(60_000)]
    [TestMethod]
    public void ResizingWrappedText_KeepsTheReaderOnTheSameLine()
    {
        // At width 22 each 60-character line is three rows, so line 5 starts at row 15.
        var viewer = Viewer(TenLines(), width: 22, height: 4);
        viewer.ScrollableModel.ScrollPosition.Y = 15;

        viewer.ViewSize = new Size(42, 4);

        Assert.AreEqual(5L, viewer.TopRow().Line, "The top of the screen is still line 5 after the lines re-flow.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ResizingWrappedText_KeepsTheReadingPositionWithinALongLine()
    {
        // At width 22 line 3 is rows 9, 10 and 11; row 10 starts at column 20.
        var viewer = Viewer(TenLines(), width: 22, height: 4);
        viewer.ScrollableModel.ScrollPosition.Y = 10;

        viewer.ViewSize = new Size(42, 4);

        Assert.AreEqual(3L, viewer.TopRow().Line, "The top row is still within line 3, not the line before or after it.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WrappedEditor_TheCaretSitsOnTheRowItIsOn()
    {
        var editor = Editor([new string('a', 50)], width: 22, height: 6);
        editor.ScrollableModel.CursorPosition.Y = 0;
        editor.ScrollableModel.CursorPosition.X = 25;

        // Row 1 of the line (columns 20 to 39), column 5 within it.
        Assert.AreEqual(1, editor.CursorPosition.Y - editor.AbsolutePosition.Y);
        Assert.AreEqual(5, editor.CursorPosition.X - editor.AbsolutePosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WrappedEditor_DownAndUp_MoveByScreenRow_KeepingTheColumn()
    {
        var editor = Editor([new string('a', 50), "short"], width: 22, height: 6);
        editor.ScrollableModel.CursorPosition.Y = 0;
        editor.ScrollableModel.CursorPosition.X = 25;

        await editor.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.AreEqual((0L, 45L), (editor.ScrollableModel.CursorPosition.Y, editor.ScrollableModel.CursorPosition.X), "Down goes to the next row of the line, at the same column in it.");

        await editor.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        Assert.AreEqual((1L, 5L), (editor.ScrollableModel.CursorPosition.Y, editor.ScrollableModel.CursorPosition.X), "The next row is the next line, whose end comes before the column.");

        await editor.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.AreEqual((0L, 45L), (editor.ScrollableModel.CursorPosition.Y, editor.ScrollableModel.CursorPosition.X));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WrappedEditor_WhenItsRowIsUnderTheBottomEdge_TheViewScrollsToIt()
    {
        var editor = Editor(TenLines(), width: 22, height: 4);
        editor.ScrollableModel.CursorPosition.Y = 0;
        editor.ScrollableModel.CursorPosition.X = 0;

        for (int step = 0; step < 6; step++)
        {
            await editor.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        }

        var cursorRow = editor.CursorPosition.Y - editor.AbsolutePosition.Y;
        Assert.IsTrue(cursorRow >= 0 && cursorRow < editor.ViewSize.Height, "The caret is on screen after moving down past the bottom.");
    }
}
