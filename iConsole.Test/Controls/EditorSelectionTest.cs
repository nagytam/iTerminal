using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Selecting text in the multi-line editor: Shift with the movement keys, and what typing and deleting do to a selection.</summary>
[TestClass]
public class EditorSelectionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextEditorControl NewEditor(string[] lines, long x, long y, bool readOnly = false, uint width = 30, uint height = 6)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, height), IsReadOnly = readOnly };
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

    private static async Task Press(TextEditorControl editor, ConsoleKey key, char c = '\0', bool shift = false, int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            await editor.HandleKeyEventAsync(new KeyEvent(c, key, shift, false, false));
        }
    }

    private static string Text(TextEditorControl editor) => string.Join("|", editor.TextModel.Lines);

    private static (long X, long Y) Cursor(TextEditorControl editor)
        => (editor.ScrollableModel.CursorPosition.X, editor.ScrollableModel.CursorPosition.Y);

    private static readonly string[] Poem = ["roses are red", "violets are blue", "sugar is sweet"];

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftRight_SelectsCharactersOnALine()
    {
        TextEditorControl editor = NewEditor(Poem, 0, 0);
        await Press(editor, ConsoleKey.RightArrow, shift: true, times: 5);
        Assert.IsTrue(editor.HasSelection);
        Assert.AreEqual("roses", editor.SelectedText);
        Assert.AreEqual((5L, 0L), Cursor(editor));
        Assert.IsTrue(editor.SelectionTextModel.IsSelected);
        Assert.AreEqual(0L, editor.SelectionTextModel.Start!.X);
        Assert.AreEqual(5L, editor.SelectionTextModel.End!.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftDown_SelectsAcrossLines()
    {
        TextEditorControl editor = NewEditor(Poem, 6, 0);
        await Press(editor, ConsoleKey.DownArrow, shift: true, times: 2);
        Assert.AreEqual("are red\nviolets are blue\nsugar ", editor.SelectedText);
        var ((startRow, startColumn), (endRow, endColumn)) = editor.SelectionRange;
        Assert.AreEqual((0L, 6L, 2L, 6L), (startRow, startColumn, endRow, endColumn));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftLeft_AtTheStartOfALine_ExtendsOverTheLineBreak()
    {
        TextEditorControl editor = NewEditor(Poem, 0, 1);
        await Press(editor, ConsoleKey.LeftArrow, shift: true, times: 3);
        Assert.AreEqual("ed\n", editor.SelectedText);
        Assert.AreEqual((11L, 0L), Cursor(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftHomeAndShiftEnd_SelectToTheEndsOfTheLine()
    {
        TextEditorControl home = NewEditor(Poem, 6, 1);
        await Press(home, ConsoleKey.Home, shift: true);
        Assert.AreEqual("violet", home.SelectedText);
        TextEditorControl end = NewEditor(Poem, 6, 1);
        await Press(end, ConsoleKey.End, shift: true);
        Assert.AreEqual("s are blue", end.SelectedText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftUp_AndABackwardsSelection_AreReportedInReadingOrder()
    {
        TextEditorControl editor = NewEditor(Poem, 4, 2);
        await Press(editor, ConsoleKey.UpArrow, shift: true);
        Assert.AreEqual("ets are blue\nsuga", editor.SelectedText);
        var (start, end) = editor.SelectionRange;
        Assert.AreEqual((1L, 4L), start);
        Assert.AreEqual((2L, 4L), end);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftPageDown_ExtendsByAPage()
    {
        string[] lines = Enumerable.Range(0, 30).Select(i => "line " + i).ToArray();
        TextEditorControl editor = NewEditor(lines, 0, 0, height: 6);
        await Press(editor, ConsoleKey.PageDown, shift: true);
        Assert.IsTrue(editor.HasSelection);
        Assert.IsTrue(editor.SelectedText.Count(c => c == '\n') >= 4);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Reversing_ShrinksTheSelection_AndEndsItWhereItBegan()
    {
        TextEditorControl editor = NewEditor(Poem, 0, 0);
        await Press(editor, ConsoleKey.RightArrow, shift: true, times: 5);
        await Press(editor, ConsoleKey.LeftArrow, shift: true, times: 2);
        Assert.AreEqual("ros", editor.SelectedText);
        await Press(editor, ConsoleKey.LeftArrow, shift: true, times: 3);
        Assert.IsFalse(editor.HasSelection);
        Assert.IsFalse(editor.SelectionTextModel.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Left_CollapsesToTheStart_AndRightToTheEnd()
    {
        TextEditorControl left = NewEditor(Poem, 2, 0);
        await Press(left, ConsoleKey.RightArrow, shift: true, times: 4);
        await Press(left, ConsoleKey.LeftArrow);
        Assert.IsFalse(left.HasSelection);
        Assert.AreEqual((2L, 0L), Cursor(left));

        TextEditorControl right = NewEditor(Poem, 2, 0);
        await Press(right, ConsoleKey.RightArrow, shift: true, times: 4);
        await Press(right, ConsoleKey.RightArrow);
        Assert.IsFalse(right.HasSelection);
        Assert.AreEqual((6L, 0L), Cursor(right));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OtherMovement_EndsTheSelection()
    {
        foreach (ConsoleKey key in new[] { ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.Home, ConsoleKey.End, ConsoleKey.PageDown })
        {
            TextEditorControl editor = NewEditor(Poem, 3, 1);
            await Press(editor, ConsoleKey.RightArrow, shift: true, times: 3);
            await Press(editor, key);
            Assert.IsFalse(editor.HasSelection, key.ToString());
            Assert.IsFalse(editor.SelectionTextModel.IsSelected, key.ToString());
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_ReplacesASelectionOnALine()
    {
        TextEditorControl editor = NewEditor(Poem, 0, 0);
        await Press(editor, ConsoleKey.RightArrow, shift: true, times: 5);
        await Press(editor, ConsoleKey.T, 't');
        Assert.AreEqual("t are red|violets are blue|sugar is sweet", Text(editor));
        Assert.AreEqual((1L, 0L), Cursor(editor));
        Assert.IsFalse(editor.HasSelection);
        Assert.IsTrue(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_ReplacesASelectionAcrossLines_JoiningWhatIsLeft()
    {
        TextEditorControl editor = NewEditor(Poem, 6, 0);
        await Press(editor, ConsoleKey.DownArrow, shift: true, times: 2);
        await Press(editor, ConsoleKey.X, 'X');
        Assert.AreEqual("roses Xis sweet", Text(editor));
        Assert.AreEqual((7L, 0L), Cursor(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BackspaceAndDelete_RemoveTheSelectionAndNothingElse()
    {
        TextEditorControl backspace = NewEditor(Poem, 6, 0);
        await Press(backspace, ConsoleKey.DownArrow, shift: true);
        await Press(backspace, ConsoleKey.Backspace);
        Assert.AreEqual("roses s are blue|sugar is sweet", Text(backspace));
        Assert.AreEqual((6L, 0L), Cursor(backspace));
        Assert.IsTrue(backspace.IsDirty);

        TextEditorControl delete = NewEditor(Poem, 0, 1);
        await Press(delete, ConsoleKey.RightArrow, shift: true, times: 7);
        await Press(delete, ConsoleKey.Delete);
        Assert.AreEqual("roses are red| are blue|sugar is sweet", Text(delete));
        Assert.AreEqual((0L, 1L), Cursor(delete));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_ReplacesTheSelectionWithALineBreak()
    {
        TextEditorControl editor = NewEditor(Poem, 5, 0);
        await Press(editor, ConsoleKey.RightArrow, shift: true, times: 5);
        await Press(editor, ConsoleKey.Enter);
        Assert.AreEqual("roses|red|violets are blue|sugar is sweet", Text(editor));
        Assert.AreEqual(4, editor.TextModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnlyEditor_CanSelectButNotChange()
    {
        TextEditorControl editor = NewEditor(Poem, 0, 0, readOnly: true);
        await Press(editor, ConsoleKey.RightArrow, shift: true, times: 5);
        Assert.AreEqual("roses", editor.SelectedText);
        await Press(editor, ConsoleKey.Backspace);
        await Press(editor, ConsoleKey.Delete);
        await Press(editor, ConsoleKey.X, 'x');
        Assert.AreEqual("roses are red|violets are blue|sugar is sweet", Text(editor));
        Assert.IsFalse(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingInTheText_EndsTheSelection()
    {
        TextEditorControl editor = NewEditor(Poem, 0, 0);
        await Press(editor, ConsoleKey.RightArrow, shift: true, times: 5);
        await editor.HandleMouseEventAsync(new MouseEvent { Position = new Position(editor.AbsolutePosition.X + 3, editor.AbsolutePosition.Y + 1), LeftClicked = true });
        Assert.IsFalse(editor.HasSelection);
        Assert.IsFalse(editor.SelectionTextModel.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSelection_IsDrawnHighlighted_OnEveryRowItCovers()
    {
        TextEditorControl editor = NewEditor(Poem, 6, 0);
        await Press(editor, ConsoleKey.DownArrow, shift: true, times: 2);
        ScreenBuffer buffer = new(80, 25);
        editor.Draw(buffer);
        ColorScheme scheme = new();
        long x = editor.AbsolutePosition.X;
        long y = editor.AbsolutePosition.Y;
        Color Background(int column, int row) => buffer.Cell((int)x + column, (int)y + row).BackgroundColor;

        Assert.AreNotEqual(scheme.SelectedFocusedBackground, Background(2, 0), "before the selection");
        Assert.AreEqual(scheme.SelectedFocusedBackground, Background(6, 0), "its first character");
        Assert.AreEqual(scheme.SelectedFocusedBackground, Background(3, 1), "a row it covers whole");
        Assert.AreEqual(scheme.SelectedFocusedBackground, Background(5, 2), "its last row");
        Assert.AreNotEqual(scheme.SelectedFocusedBackground, Background(7, 2), "after the selection");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SelectingPastTheBottomOfTheView_ScrollsToKeepTheCursorInSight()
    {
        string[] lines = Enumerable.Range(0, 30).Select(i => "line " + i).ToArray();
        TextEditorControl editor = NewEditor(lines, 0, 0, height: 6);
        await Press(editor, ConsoleKey.DownArrow, shift: true, times: 20);
        long cursor = editor.ScrollableModel.CursorPosition.Y;
        long scroll = editor.ScrollableModel.ScrollPosition.Y;
        Assert.AreEqual(20L, cursor);
        Assert.IsTrue(cursor >= scroll && cursor < scroll + editor.ScrollableModel.ViewSize.Height, $"cursor {cursor}, scroll {scroll}");
        Assert.AreEqual(20, editor.SelectedText.Count(c => c == '\n'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EmptyDocument_ShiftMovementIsHarmless()
    {
        TextEditorControl editor = NewEditor([], 0, 0);
        foreach (ConsoleKey key in new[] { ConsoleKey.LeftArrow, ConsoleKey.RightArrow, ConsoleKey.UpArrow, ConsoleKey.DownArrow, ConsoleKey.Home, ConsoleKey.End })
        {
            await Press(editor, key, shift: true);
        }

        Assert.IsFalse(editor.HasSelection);
        Assert.AreEqual(string.Empty, editor.SelectedText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftWithAnotherKey_IsNotASelection()
    {
        TextEditorControl editor = NewEditor(Poem, 0, 0);
        await Press(editor, ConsoleKey.A, 'A', shift: true);
        Assert.AreEqual("Aroses are red|violets are blue|sugar is sweet", Text(editor));
        Assert.IsFalse(editor.HasSelection);
    }
}
