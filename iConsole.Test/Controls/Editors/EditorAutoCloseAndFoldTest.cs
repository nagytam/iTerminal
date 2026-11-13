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

/// <summary>Auto-close of brackets and quotes in the editor, and folding every region with Alt+Shift+Left and Alt+Shift+Right.</summary>
[TestClass]
public class EditorAutoCloseAndFoldTest
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

    private static async Task Type(TextEditorControl editor, string text)
    {
        foreach (char c in text)
        {
            await Press(editor, ConsoleKey.A, c);
        }
    }

    private static string Text(TextEditorControl editor) => string.Join("|", editor.TextModel.Lines);

    private static void Cursor(TextEditorControl editor, long row, long column)
    {
        editor.ScrollableModel.CursorPosition.Y = row;
        editor.ScrollableModel.CursorPosition.X = column;
    }

    // ---- auto-close ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingAnOpeningBracket_AddsTheClosingOne_WithTheCursorBetween()
    {
        TextEditorControl editor = NewEditor(40, 6, "");
        await Type(editor, "(");
        Assert.AreEqual("()", Text(editor));
        Assert.AreEqual(1L, editor.ScrollableModel.CursorPosition.X);

        await Type(editor, "x");
        Assert.AreEqual("(x)", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingAClosingBracket_StepsOverTheOneThatIsAlreadyThere()
    {
        TextEditorControl editor = NewEditor(40, 6, "");
        await Type(editor, "[a]");
        Assert.AreEqual("[a]", Text(editor));
        Assert.AreEqual(3L, editor.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EveryBracketKind_IsPaired()
    {
        TextEditorControl editor = NewEditor(40, 6, "");
        await Type(editor, "{");
        Assert.AreEqual("{}", Text(editor));
        await Type(editor, "}");
        Assert.AreEqual("{}", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Quotes_ArePaired_ButAnApostropheAfterALetterIsLeftAlone()
    {
        TextEditorControl editor = NewEditor(40, 6, "");
        await Type(editor, "\"hi\"");
        Assert.AreEqual("\"hi\"", Text(editor));

        TextEditorControl second = NewEditor(40, 6, "");
        await Type(second, "don't");
        Assert.AreEqual("don't", Text(second));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpeningBeforeAWord_DoesNotPair()
    {
        TextEditorControl editor = NewEditor(40, 6, "word");
        Cursor(editor, 0, 0);
        await Type(editor, "(");
        Assert.AreEqual("(word", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_BetweenAPair_RemovesBothHalves()
    {
        TextEditorControl editor = NewEditor(40, 6, "");
        await Type(editor, "(");
        await Press(editor, ConsoleKey.Backspace);
        Assert.AreEqual("", Text(editor));
        Assert.AreEqual(0L, editor.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AutoClose_IsOneUndoStep()
    {
        TextEditorControl editor = NewEditor(40, 6, "");
        await Type(editor, "(");
        Assert.IsTrue(editor.CanUndo);
        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhenTurnedOff_TypingIsPlain()
    {
        TextEditorControl editor = NewEditor(40, 6, "");
        editor.AutoCloseBrackets = false;
        await Type(editor, "(");
        Assert.AreEqual("(", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnly_TypingIsRefused()
    {
        TextEditorControl editor = NewEditor(40, 6, "x");
        editor.IsReadOnly = true;
        Cursor(editor, 0, 1);
        await Type(editor, "(");
        Assert.AreEqual("x", Text(editor));
    }

    // ---- fold all and open all ----

    private static TextEditorControl FoldableEditor()
    {
        TextEditorControl editor = NewEditor(40, 10, "top", "  one", "  two", "end", "  more", "  lines");
        editor.EnableFolding = true;
        return editor;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltShiftLeft_FoldsEveryRegion_AndAltShiftRightOpensThemAll()
    {
        TextEditorControl editor = FoldableEditor();
        Cursor(editor, 0, 0);

        await Press(editor, ConsoleKey.LeftArrow, shift: true, alt: true);
        Assert.IsTrue(editor.IsFolded(0), "the region of the first line is folded");
        Assert.IsTrue(editor.IsFolded(3), "and so is the second");

        await Press(editor, ConsoleKey.RightArrow, shift: true, alt: true);
        Assert.AreEqual(0, editor.FoldedCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltLeft_StillFoldsOnlyTheRegionAtTheCursor()
    {
        TextEditorControl editor = FoldableEditor();
        Cursor(editor, 0, 0);

        await Press(editor, ConsoleKey.LeftArrow, alt: true);
        Assert.IsTrue(editor.IsFolded(0));
        Assert.IsFalse(editor.IsFolded(3), "the other region is left open");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FoldAllWithFoldingOff_DoesNothing()
    {
        TextEditorControl editor = NewEditor(40, 10, "top", "  one");
        editor.EnableFolding = false;
        Cursor(editor, 0, 0);
        await Press(editor, ConsoleKey.LeftArrow, shift: true, alt: true);
        Assert.AreEqual(0, editor.FoldedCount);
    }
}
