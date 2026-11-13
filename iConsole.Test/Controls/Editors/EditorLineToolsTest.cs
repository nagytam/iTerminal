using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Editors;

/// <summary>The line tools (move, duplicate, comment, trim, join, remove, tabs), the keys that run them, and the whitespace and edge-column marks.</summary>
[TestClass]
public class EditorLineToolsTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextEditorControl NewEditor(int width, int height, params string[] lines)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
        root.AddControl(editor);
        foreach (string line in lines)
        {
            editor.TextModel.Lines.Add(line);
        }

        editor.OnInitializeLayout();
        editor.Refresh();
        editor.SetFocus();
        return editor;
    }

    private static Task Press(TextEditorControl editor, ConsoleKey key, char c = '\0', bool shift = false, bool alt = false, bool control = false)
        => editor.HandleKeyEventAsync(new KeyEvent(c, key, shift, alt, control));

    private static string Text(TextEditorControl editor) => string.Join("|", editor.TextModel.Lines);

    private static void Cursor(TextEditorControl editor, long row, long column)
    {
        editor.ScrollableModel.CursorPosition.Y = row;
        editor.ScrollableModel.CursorPosition.X = column;
    }

    /// <summary>Selects whole rows <paramref name="from"/> to <paramref name="to"/> (inclusive) with Shift+Down and Shift+End, as a user does.</summary>
    private static async Task SelectRows(TextEditorControl editor, int from, int to)
    {
        Cursor(editor, from, 0);
        for (var row = from; row < to; row++)
        {
            await Press(editor, ConsoleKey.DownArrow, shift: true);
        }

        await Press(editor, ConsoleKey.End, shift: true);
    }

    private static ScreenBuffer Draw(TextEditorControl editor)
    {
        var screen = new ScreenBuffer((int)editor.ViewSize.Width, (int)editor.ViewSize.Height);
        editor.Draw(screen);
        editor.Draw(screen);
        return screen;
    }

    private static SyntaxLanguage Language(string marker) => new("test", [], [marker]);

    // ---- move ----

    [Timeout(60_000)]
    [TestMethod]
    public void MoveLines_SwapsTheCursorLineWithItsNeighbourAndFollowsIt()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "b", "c");
        Cursor(editor, 1, 0);

        Assert.IsTrue(editor.MoveLines(down: true));
        Assert.AreEqual("a|c|b", Text(editor));
        Assert.AreEqual(2L, editor.ScrollableModel.CursorPosition.Y);

        Assert.IsTrue(editor.MoveLines(down: false));
        Assert.AreEqual("a|b|c", Text(editor));
        Assert.AreEqual(1L, editor.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveLines_DoesNothingAtTheEdgesOfTheFile()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "b");
        Cursor(editor, 0, 0);
        Assert.IsFalse(editor.MoveLines(down: false));
        Cursor(editor, 1, 0);
        Assert.IsFalse(editor.MoveLines(down: true));
        Assert.AreEqual("a|b", Text(editor));
        Assert.IsFalse(editor.CanUndo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MoveLines_MovesTheSelectedBlockAndKeepsItSelected()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "b", "c", "d");
        await SelectRows(editor, 0, 1);
        Assert.IsTrue(editor.HasSelection);

        Assert.IsTrue(editor.MoveLines(down: true));
        Assert.AreEqual("c|a|b|d", Text(editor));
        Assert.IsTrue(editor.HasSelection, "the moved block stays selected");
        Assert.AreEqual(1L, editor.SelectionRange.Start.Row);
        Assert.AreEqual(2L, editor.SelectionRange.End.Row);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltDownAndAltUp_MoveTheLine_AndUndoBringsItBack()
    {
        TextEditorControl editor = NewEditor(30, 6, "one", "two");
        Cursor(editor, 0, 0);

        await Press(editor, ConsoleKey.DownArrow, alt: true);
        Assert.AreEqual("two|one", Text(editor));

        await Press(editor, ConsoleKey.UpArrow, alt: true);
        Assert.AreEqual("one|two", Text(editor));

        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("two|one", Text(editor));
    }

    // ---- duplicate ----

    [Timeout(60_000)]
    [TestMethod]
    public void DuplicateLines_CopiesTheCursorLineBelowAndMovesDownWithIt()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "b");
        Cursor(editor, 0, 0);

        Assert.IsTrue(editor.DuplicateLines());
        Assert.AreEqual("a|a|b", Text(editor));
        Assert.AreEqual(1L, editor.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DuplicateLines_WithASelection_CopiesTheBlockAndSelectsTheCopy()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "b", "c");
        await SelectRows(editor, 0, 1);

        Assert.IsTrue(editor.DuplicateLines());
        Assert.AreEqual("a|b|a|b|c", Text(editor));
        Assert.AreEqual(2L, editor.SelectionRange.Start.Row);
        Assert.AreEqual(3L, editor.SelectionRange.End.Row);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlD_DuplicatesTheLine()
    {
        TextEditorControl editor = NewEditor(30, 6, "x");
        Cursor(editor, 0, 0);
        await Press(editor, ConsoleKey.D, control: true);
        Assert.AreEqual("x|x", Text(editor));
    }

    // ---- comment ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task ToggleLineComment_AddsTheMarkerAtTheCommonIndent_AndRemovesItAgain()
    {
        TextEditorControl editor = NewEditor(30, 6, "    int a;", "      int b;", "");
        editor.Syntax = Language("//");
        await SelectRows(editor, 0, 2);

        Assert.IsTrue(editor.ToggleLineComment());
        Assert.AreEqual("    // int a;|    //   int b;|", Text(editor), "the blank line is left alone and the block keeps its shape");

        Assert.IsTrue(editor.ToggleLineComment());
        Assert.AreEqual("    int a;|      int b;|", Text(editor), "the marker and the space after it come off");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ToggleLineComment_WhenNotEveryLineIsCommented_CommentsAllOfThem()
    {
        TextEditorControl editor = NewEditor(30, 6, "// done", "todo");
        editor.Syntax = Language("//");
        await SelectRows(editor, 0, 1);

        Assert.IsTrue(editor.ToggleLineComment());
        Assert.AreEqual("// // done|// todo", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ToggleLineComment_DoesNothingWithoutALineCommentInTheLanguage()
    {
        TextEditorControl editor = NewEditor(30, 6, "text");
        Cursor(editor, 0, 0);
        Assert.IsFalse(editor.ToggleLineComment(), "no language");

        editor.Syntax = new SyntaxLanguage("plain", [], []);
        Assert.IsFalse(editor.ToggleLineComment(), "a language without a line comment");
        Assert.AreEqual("text", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlQ_TogglesTheCommentOnTheCursorLine()
    {
        TextEditorControl editor = NewEditor(30, 6, "x = 1");
        editor.Syntax = Language("#");
        Cursor(editor, 0, 0);
        await Press(editor, ConsoleKey.Q, control: true);
        Assert.AreEqual("# x = 1", Text(editor));
    }

    // ---- utilities ----

    [Timeout(60_000)]
    [TestMethod]
    public void TrimTrailingWhitespace_WholeFileWithNoSelection()
    {
        TextEditorControl editor = NewEditor(30, 6, "a  ", "b\t", "c");
        Assert.IsTrue(editor.TrimTrailingWhitespace());
        Assert.AreEqual("a|b|c", Text(editor));
        Assert.IsFalse(editor.TrimTrailingWhitespace(), "nothing left to trim");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemoveBlankLines_RemovesWhitespaceOnlyLines()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "", "  \t", "b");
        Assert.IsTrue(editor.RemoveBlankLines());
        Assert.AreEqual("a|b", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemoveBlankLines_OfAFileThatIsAllBlank_LeavesOneEmptyLine()
    {
        TextEditorControl editor = NewEditor(30, 6, "", " ");
        Assert.IsTrue(editor.RemoveBlankLines());
        Assert.AreEqual(1, editor.TextModel.Lines.Count);
        Assert.AreEqual("", editor.TextModel.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemoveDuplicateLines_KeepsTheFirstOfEachAndIsCaseSensitive()
    {
        TextEditorControl editor = NewEditor(30, 6, "b", "a", "b", "A", "a");
        Assert.IsTrue(editor.RemoveDuplicateLines());
        Assert.AreEqual("b|a|A", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TabsToSpaces_PadsToTheNextMultipleOfTheIndentWidth()
    {
        TextEditorControl editor = NewEditor(30, 6, "a\tb", "\t\tc");
        Assert.IsTrue(editor.TabsToSpaces());
        Assert.AreEqual("a   b|" + new string(' ', 8) + "c", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task JoinLines_OfASelection_RunsTheLinesTogetherWithOneSpace()
    {
        TextEditorControl editor = NewEditor(30, 6, "one  ", "   two", "", "three", "four");
        await SelectRows(editor, 0, 3);

        Assert.IsTrue(editor.JoinLines());
        Assert.AreEqual("one two three|four", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void JoinLines_WithNoSelection_JoinsTheCursorLineToTheOneBelow()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "b", "c");
        Cursor(editor, 0, 0);
        Assert.IsTrue(editor.JoinLines());
        Assert.AreEqual("a b|c", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void JoinLines_OnTheLastLine_DoesNothing()
    {
        TextEditorControl editor = NewEditor(30, 6, "a", "b");
        Cursor(editor, 1, 0);
        Assert.IsFalse(editor.JoinLines());
        Assert.AreEqual("a|b", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLineTool_IsOneUndoStep()
    {
        TextEditorControl editor = NewEditor(30, 6, "a  ", "", "a  ");
        Assert.IsTrue(editor.RemoveBlankLines());
        Assert.IsTrue(editor.TrimTrailingWhitespace());
        Assert.AreEqual("a|a", Text(editor));

        Assert.IsTrue(editor.Undo());
        Assert.AreEqual("a  |a  ", Text(editor), "the trim is taken back first");
        Assert.IsTrue(editor.Undo());
        Assert.AreEqual("a  ||a  ", Text(editor), "then the blank line removal");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReadOnlyEditor_RefusesEveryLineTool()
    {
        TextEditorControl editor = NewEditor(30, 6, "a  ", "a  ");
        editor.IsReadOnly = true;
        Cursor(editor, 0, 0);

        Assert.IsFalse(editor.MoveLines(down: true));
        Assert.IsFalse(editor.DuplicateLines());
        Assert.IsFalse(editor.TrimTrailingWhitespace());
        Assert.IsFalse(editor.RemoveDuplicateLines());
        Assert.IsFalse(editor.JoinLines());
        Assert.AreEqual("a  |a  ", Text(editor));
    }

    // ---- whitespace and edge column ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlShiftL_ShowsTheLineEndAndTheWhitespace_AgainHidesThem()
    {
        TextEditorControl editor = NewEditor(30, 5, "ab", "x");
        Cursor(editor, 1, 0);

        await Press(editor, ConsoleKey.L, shift: true, control: true);
        Assert.IsTrue(editor.ShowWhitespace);
        Assert.IsTrue(Draw(editor).Line(0).StartsWith("ab¶"), Draw(editor).Line(0));

        await Press(editor, ConsoleKey.L, shift: true, control: true);
        Assert.IsFalse(editor.ShowWhitespace);
        Assert.IsFalse(Draw(editor).Line(0).Contains('¶'));
        Assert.AreEqual("ab", editor.TextModel.Lines[0], "the text is not changed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlShiftI_MarksTheEightiethColumn_AndAgainHidesIt()
    {
        TextEditorControl editor = NewEditor(30, 5, "abc", "x");
        Cursor(editor, 1, 0);
        ColorScheme scheme = new();

        await Press(editor, ConsoleKey.I, shift: true, control: true);
        Assert.AreEqual(TextEditorControl.DefaultEdgeColumn, editor.EdgeColumn);
        // The viewer is 30 cells wide, so column 80 is off screen: nothing is marked and the text is untouched.
        Assert.AreEqual("abc", Draw(editor).Line(0).TrimEnd());

        editor.EdgeColumn = 5;
        Assert.AreEqual(scheme.DisabledForeground, Draw(editor).BackgroundAt(4, 0), "column 5 takes the edge colour");

        await Press(editor, ConsoleKey.I, shift: true, control: true);
        Assert.AreEqual(0, editor.EdgeColumn);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EdgeColumn_IsNeverNegative()
    {
        TextEditorControl editor = NewEditor(30, 5, "x");
        editor.EdgeColumn = -4;
        Assert.AreEqual(0, editor.EdgeColumn);
    }
}
