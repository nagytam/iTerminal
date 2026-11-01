using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Typing, Enter, Backspace and Delete in the text editor, and the viewer at tiny sizes.</summary>
[TestClass]
public class EditorTypingTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextEditorControl NewEditor(string[] lines, long x, long y)
    {
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 5) };
        editor.SetFocus();
        foreach (string line in lines)
        {
            editor.TextModel.Lines.Add(line);
        }

        editor.ScrollableModel.CursorPosition.X = x;
        editor.ScrollableModel.CursorPosition.Y = y;
        return editor;
    }

    private static Task Press(TextEditorControl editor, ConsoleKey key, char c = '\0')
        => editor.HandleKeyEventAsync(new KeyEvent(c, key, false, false, false));

    private static string Text(TextEditorControl editor) => string.Join("|", editor.TextModel.Lines);

    private static (long X, long Y) Cursor(TextEditorControl editor)
        => (editor.ScrollableModel.CursorPosition.X, editor.ScrollableModel.CursorPosition.Y);

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_RemovesTheCharacterBeforeTheCursor()
    {
        TextEditorControl editor = NewEditor(["abc"], 3, 0);
        await Press(editor, ConsoleKey.Backspace);
        Assert.AreEqual("ab", Text(editor));
        Assert.AreEqual((2L, 0L), Cursor(editor));
        await Press(editor, ConsoleKey.LeftArrow);
        await Press(editor, ConsoleKey.Backspace);
        Assert.AreEqual("b", Text(editor));
        Assert.IsTrue(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_AtTheStartOfALine_JoinsItOntoTheOneAbove()
    {
        TextEditorControl editor = NewEditor(["ab", "cd"], 0, 1);
        await Press(editor, ConsoleKey.Backspace);
        Assert.AreEqual("abcd", Text(editor));
        Assert.AreEqual((2L, 0L), Cursor(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_AtTheStartOfTheDocument_ChangesNothing()
    {
        TextEditorControl editor = NewEditor(["ab"], 0, 0);
        await Press(editor, ConsoleKey.Backspace);
        Assert.AreEqual("ab", Text(editor));
        Assert.IsFalse(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Delete_RemovesTheCharacterUnderTheCursor_AndPullsTheNextLineUpAtTheEnd()
    {
        TextEditorControl editor = NewEditor(["abc", "d"], 1, 0);
        await Press(editor, ConsoleKey.Delete);
        Assert.AreEqual("ac|d", Text(editor));
        Assert.AreEqual((1L, 0L), Cursor(editor));
        await Press(editor, ConsoleKey.End);
        editor.ScrollableModel.CursorPosition.X = 2;
        await Press(editor, ConsoleKey.Delete);
        Assert.AreEqual("acd", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Delete_AtTheEndOfTheDocument_ChangesNothing()
    {
        TextEditorControl editor = NewEditor(["ab"], 2, 0);
        await Press(editor, ConsoleKey.Delete);
        Assert.AreEqual("ab", Text(editor));
        Assert.IsFalse(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_SplitsTheLineAtTheCursorAndMovesToTheNewLine()
    {
        TextEditorControl editor = NewEditor(["abc"], 1, 0);
        await Press(editor, ConsoleKey.Enter);
        Assert.AreEqual("a|bc", Text(editor));
        Assert.AreEqual((0L, 1L), Cursor(editor));
        await Press(editor, ConsoleKey.Enter);
        Assert.AreEqual("a||bc", Text(editor));
        Assert.AreEqual((0L, 2L), Cursor(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_AtTheEndAndAtTheStartOfALine()
    {
        TextEditorControl atEnd = NewEditor(["abc"], 3, 0);
        await Press(atEnd, ConsoleKey.Enter);
        Assert.AreEqual("abc|", Text(atEnd));
        TextEditorControl atStart = NewEditor(["abc"], 0, 0);
        await Press(atStart, ConsoleKey.Enter);
        Assert.AreEqual("|abc", Text(atStart));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EmptyDocument_AcceptsEnterBackspaceDeleteAndTyping()
    {
        TextEditorControl editor = NewEditor([], 0, 0);
        await Press(editor, ConsoleKey.Backspace);
        await Press(editor, ConsoleKey.Delete);
        await Press(editor, ConsoleKey.Enter);
        Assert.AreEqual("|", Text(editor));
        TextEditorControl typing = NewEditor([], 0, 0);
        await Press(typing, ConsoleKey.A, 'a');
        Assert.AreEqual("a", Text(typing));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_InsertsAtTheCursor_AndAClampedCursorPastTheLineEnd()
    {
        TextEditorControl editor = NewEditor(["ac"], 1, 0);
        await Press(editor, ConsoleKey.B, 'b');
        Assert.AreEqual("abc", Text(editor));
        Assert.AreEqual((2L, 0L), Cursor(editor));
        TextEditorControl pastEnd = NewEditor(["ab"], 9, 0);
        await Press(pastEnd, ConsoleKey.C, 'c');
        Assert.AreEqual("abc", Text(pastEnd));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnlyEditor_IgnoresEditingKeys()
    {
        TextEditorControl editor = NewEditor(["abc"], 1, 0);
        editor.IsReadOnly = true;
        await Press(editor, ConsoleKey.Delete);
        await Press(editor, ConsoleKey.Enter);
        await Press(editor, ConsoleKey.X, 'x');
        Assert.AreEqual("abc", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextViewer_AtTinySizes_DrawsWithoutNegativeSizes()
    {
        foreach ((uint width, uint height) in new[] { (0u, 0u), (1u, 1u), (2u, 1u), (1u, 3u), (3u, 1u) })
        {
            TextViewerControl viewer = new(false, Theme()) { Position = new Position(0, 0), ViewSize = new Size(width, height) };
            viewer.TextModel.Lines.Add("hello");
            viewer.OnInitializeLayout();
            viewer.Draw(new ScreenBuffer((int)Math.Max(1, width), (int)Math.Max(1, height)));
        }
    }
}
