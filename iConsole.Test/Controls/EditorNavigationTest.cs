using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Moving the caret in the text editor: across lines, to the line end, onto shorter lines, and past the view edge.</summary>
[TestClass]
public class EditorNavigationTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextEditorControl NewEditor(string[] lines, long x, long y, uint width = 20, uint height = 5)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
        root.AddControl(editor);
        foreach (string line in lines)
        {
            editor.TextModel.Lines.Add(line);
        }

        editor.OnInitializeLayout();
        editor.Refresh();
        editor.SetFocus();
        editor.ScrollableModel.CursorPosition.X = x;
        editor.ScrollableModel.CursorPosition.Y = y;
        return editor;
    }

    private static Task Press(TextEditorControl editor, ConsoleKey key, char c = '\0')
        => editor.HandleKeyEventAsync(new KeyEvent(c, key, false, false, false));

    private static (long X, long Y) Cursor(TextEditorControl editor)
        => (editor.ScrollableModel.CursorPosition.X, editor.ScrollableModel.CursorPosition.Y);

    [TestMethod]
    public async Task Left_AtTheStartOfALine_GoesToTheEndOfTheLineAbove()
    {
        TextEditorControl editor = NewEditor(["abc", "de"], 0, 1);
        await Press(editor, ConsoleKey.LeftArrow);
        Assert.AreEqual((3L, 0L), Cursor(editor));
    }

    [TestMethod]
    public async Task Right_AtTheEndOfALine_GoesToTheStartOfTheNextLine()
    {
        TextEditorControl editor = NewEditor(["abc", "de"], 3, 0);
        await Press(editor, ConsoleKey.RightArrow);
        Assert.AreEqual((0L, 1L), Cursor(editor));
    }

    [TestMethod]
    public async Task LeftAndRight_AtTheEndsOfTheDocument_StayPut()
    {
        TextEditorControl start = NewEditor(["abc"], 0, 0);
        await Press(start, ConsoleKey.LeftArrow);
        Assert.AreEqual((0L, 0L), Cursor(start));
        TextEditorControl end = NewEditor(["abc"], 3, 0);
        await Press(end, ConsoleKey.RightArrow);
        Assert.AreEqual((3L, 0L), Cursor(end));
    }

    [TestMethod]
    public async Task Right_WithinALine_MovesOneColumnAndStopsAtTheLineEnd()
    {
        TextEditorControl editor = NewEditor(["ab", "longer line"], 1, 0);
        await Press(editor, ConsoleKey.RightArrow);
        Assert.AreEqual((2L, 0L), Cursor(editor));
        TextEditorControl last = NewEditor(["ab", "longer line"], 11, 1);
        await Press(last, ConsoleKey.RightArrow);
        Assert.AreEqual((11L, 1L), Cursor(last));
    }

    [TestMethod]
    public async Task End_GoesToTheEndOfTheLineTheCursorIsOn()
    {
        TextEditorControl editor = NewEditor(["abcdef", "xy", "123"], 0, 1);
        await Press(editor, ConsoleKey.End);
        Assert.AreEqual((2L, 1L), Cursor(editor));
    }

    [TestMethod]
    public async Task Home_GoesToTheStartOfTheLine()
    {
        TextEditorControl editor = NewEditor(["abcdef", "xyz"], 2, 1);
        await Press(editor, ConsoleKey.Home);
        Assert.AreEqual((0L, 1L), Cursor(editor));
    }

    [TestMethod]
    public async Task UpAndDown_OntoAShorterLine_BringTheCursorBackToItsEnd()
    {
        TextEditorControl editor = NewEditor(["abcdef", "xy", "abcdef"], 5, 0);
        await Press(editor, ConsoleKey.DownArrow);
        Assert.AreEqual((2L, 1L), Cursor(editor));
        TextEditorControl up = NewEditor(["xy", "abcdef"], 6, 1);
        await Press(up, ConsoleKey.UpArrow);
        Assert.AreEqual((2L, 0L), Cursor(up));
    }

    [TestMethod]
    public async Task Typing_PastTheRightEdge_ScrollsTheViewToKeepTheCursorOnScreen()
    {
        TextEditorControl editor = NewEditor([string.Empty], 0, 0, width: 20);
        for (int i = 0; i < 40; i++)
        {
            await Press(editor, ConsoleKey.A, 'a');
        }

        long scroll = editor.ScrollableModel.ScrollPosition.X;
        long cursor = editor.ScrollableModel.CursorPosition.X;
        Assert.AreEqual(40L, cursor);
        Assert.IsTrue(scroll > 0, "the view should have scrolled");
        Assert.IsTrue(cursor - scroll >= 0 && cursor - scroll < editor.ScrollableModel.ViewSize.Width, $"cursor {cursor} scroll {scroll}");
    }

    [TestMethod]
    public async Task Right_AcrossALongLine_ScrollsTheViewToKeepTheCursorOnScreen()
    {
        TextEditorControl editor = NewEditor([new string('x', 60)], 0, 0, width: 20);
        for (int i = 0; i < 45; i++)
        {
            await Press(editor, ConsoleKey.RightArrow);
        }

        long scroll = editor.ScrollableModel.ScrollPosition.X;
        long cursor = editor.ScrollableModel.CursorPosition.X;
        Assert.AreEqual(45L, cursor);
        Assert.IsTrue(cursor - scroll >= 0 && cursor - scroll < editor.ScrollableModel.ViewSize.Width, $"cursor {cursor} scroll {scroll}");
    }

    [TestMethod]
    public async Task Down_PastTheBottomEdge_ScrollsTheViewVertically()
    {
        TextEditorControl editor = NewEditor(Enumerable.Range(0, 12).Select(i => "line " + i).ToArray(), 0, 0, height: 5);
        for (int i = 0; i < 9; i++)
        {
            await Press(editor, ConsoleKey.DownArrow);
        }

        long scroll = editor.ScrollableModel.ScrollPosition.Y;
        long cursor = editor.ScrollableModel.CursorPosition.Y;
        Assert.AreEqual(9L, cursor);
        Assert.IsTrue(cursor - scroll >= 0 && cursor - scroll < editor.ScrollableModel.ViewSize.Height, $"cursor {cursor} scroll {scroll}");
    }

    [TestMethod]
    public void ScrollToVisible_PutsTheCursorOnTheLastColumnWhenItIsPastTheRightEdge()
    {
        ScrollableModel model = new() { ViewSize = new Size(10, 5), ScrollableSize = new Size(100, 5) };
        model.CursorPosition.X = 25;
        model.ScrollToVisible();
        Assert.AreEqual(16L, model.ScrollPosition.X);
        Assert.AreEqual(9L, model.CursorPosition.X - model.ScrollPosition.X);
        model.CursorPosition.X = 3;
        model.ScrollToVisible();
        Assert.AreEqual(3L, model.ScrollPosition.X);
    }

    [TestMethod]
    public async Task EmptyDocument_NavigationKeysAreHarmless()
    {
        TextEditorControl editor = NewEditor([], 0, 0);
        foreach (ConsoleKey key in new[] { ConsoleKey.LeftArrow, ConsoleKey.RightArrow, ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.Home, ConsoleKey.End, ConsoleKey.PageDown, ConsoleKey.PageUp })
        {
            await Press(editor, key);
        }

        Assert.AreEqual((0L, 0L), Cursor(editor));
    }
}
