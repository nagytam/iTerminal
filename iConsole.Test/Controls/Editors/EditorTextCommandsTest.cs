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

/// <summary>The line commands (case, sort, line ending), mark all, and the reload of the whole text from disk, in the editor.</summary>
[TestClass]
public class EditorTextCommandsTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextEditorControl NewEditor(params string[] lines)
    {
        // Laid out in a window, as the app lays it out: a shift-selection is lost when the editor has no size yet.
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 8) };
        root.AddControl(editor);
        foreach (string line in lines)
        {
            editor.TextModel.Lines.Add(line);
        }

        editor.TextModel.LineEnding = "\n";
        editor.OnInitializeLayout();
        editor.Refresh();
        editor.SetFocus();
        return editor;
    }

    private static Task Press(TextEditorControl editor, ConsoleKey key, char c = '\0', bool shift = false, bool control = false)
        => editor.HandleKeyEventAsync(new KeyEvent(c, key, shift, false, control));

    private static async Task Select(TextEditorControl editor, ConsoleKey direction, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Press(editor, direction, shift: true);
        }
    }

    private static string Text(TextEditorControl editor) => string.Join("|", editor.TextModel.Lines);

    // ---- Case ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangeCase_Upper_ChangesOnlyTheSelectedCharacters()
    {
        TextEditorControl editor = NewEditor("hello world");
        await Select(editor, ConsoleKey.RightArrow, 5);

        Assert.IsTrue(editor.ChangeCase(upper: true));
        Assert.AreEqual("HELLO world", Text(editor));
        Assert.IsTrue(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangeCase_Lower_StopsAtTheSelectionEndOnTheLastLine()
    {
        TextEditorControl editor = NewEditor("ABC", "DEF", "GHI");
        await Select(editor, ConsoleKey.DownArrow, 1);
        await Select(editor, ConsoleKey.RightArrow, 2);

        Assert.IsTrue(editor.ChangeCase(upper: false));
        Assert.AreEqual("abc|deF|GHI", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ChangeCase_WithNothingSelected_DoesNothing()
    {
        TextEditorControl editor = NewEditor("hello");

        Assert.IsFalse(editor.ChangeCase(upper: true));
        Assert.AreEqual("hello", Text(editor));
        Assert.IsFalse(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangeCase_ReadOnly_Refuses()
    {
        TextEditorControl editor = NewEditor("hello");
        await Select(editor, ConsoleKey.RightArrow, 5);
        editor.IsReadOnly = true;

        Assert.IsFalse(editor.ChangeCase(upper: true));
        Assert.AreEqual("hello", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangeCase_Undo_TakesItBack()
    {
        TextEditorControl editor = NewEditor("hello");
        await Select(editor, ConsoleKey.RightArrow, 5);
        editor.ChangeCase(upper: true);

        Assert.IsTrue(editor.Undo());
        Assert.AreEqual("hello", Text(editor));
    }

    // ---- Sort ----

    [Timeout(60_000)]
    [TestMethod]
    public void SortLines_WholeFileWhenNothingIsSelected_Ascending()
    {
        TextEditorControl editor = NewEditor("pear", "apple", "fig");

        Assert.IsTrue(editor.SortLines(descending: false));
        Assert.AreEqual("apple|fig|pear", Text(editor));
        Assert.IsTrue(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SortLines_Descending()
    {
        TextEditorControl editor = NewEditor("pear", "apple", "fig");

        Assert.IsTrue(editor.SortLines(descending: true));
        Assert.AreEqual("pear|fig|apple", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SortLines_OnlyTheSelectedLines_AColumnZeroEndLeavesTheLineAlone()
    {
        // The selection runs down to column 0 of the third line, which it has not touched.
        TextEditorControl editor = NewEditor("c", "b", "a", "z");
        await Select(editor, ConsoleKey.DownArrow, 2);

        Assert.IsTrue(editor.SortLines(descending: false));
        Assert.AreEqual("b|c|a|z", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SortLines_AlreadySorted_ChangesNothing()
    {
        TextEditorControl editor = NewEditor("a", "b", "c");

        Assert.IsFalse(editor.SortLines(descending: false));
        Assert.IsFalse(editor.IsDirty);
        Assert.IsFalse(editor.CanUndo);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SortLines_Undo_RestoresTheOrder()
    {
        TextEditorControl editor = NewEditor("b", "a");
        editor.SortLines(descending: false);

        Assert.IsTrue(editor.Undo());
        Assert.AreEqual("b|a", Text(editor));
    }

    // ---- Line ending ----

    [Timeout(60_000)]
    [TestMethod]
    public void CycleLineEnding_MovesThroughCrLfLfCrAndMarksTheFileChanged()
    {
        TextEditorControl editor = NewEditor("x");

        Assert.AreEqual("CR", editor.CycleLineEnding());
        Assert.AreEqual("\r", editor.TextModel.LineEnding);
        Assert.AreEqual("CRLF", editor.CycleLineEnding());
        Assert.AreEqual("LF", editor.CycleLineEnding());
        Assert.AreEqual("\n", editor.TextModel.LineEnding);
        Assert.IsTrue(editor.IsDirty);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CycleLineEnding_ReadOnly_KeepsIt()
    {
        TextEditorControl editor = NewEditor("x");
        editor.IsReadOnly = true;

        Assert.AreEqual("LF", editor.CycleLineEnding());
        Assert.AreEqual("\n", editor.TextModel.LineEnding);
    }

    // ---- Mark all ----

    [Timeout(60_000)]
    [TestMethod]
    public void MarkAll_MarksEveryMatchOfTheLastSearch()
    {
        TextEditorControl editor = NewEditor("ab ab", "xab", "AB");
        editor.LastFind = "ab";
        editor.LastMatchCase = true;

        Assert.AreEqual(3, editor.ToggleMarkAll());
        Assert.AreEqual(3, editor.Decorations.Count);
        Assert.AreEqual(2, editor.Decorations.ForLine(0).Count);
        Assert.AreEqual(TextEditorControl.MarkAllSource, editor.Decorations.ForLine(0)[0].Source);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkAll_IgnoresCaseWhenTheSearchDoesNot()
    {
        TextEditorControl editor = NewEditor("Ab aB");
        editor.LastFind = "ab";
        editor.LastMatchCase = false;

        Assert.AreEqual(2, editor.ToggleMarkAll());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MarkAll_MarksTheSelectionWhenThereIsOne()
    {
        TextEditorControl editor = NewEditor("abc abc");
        await Select(editor, ConsoleKey.RightArrow, 3);

        Assert.AreEqual(2, editor.ToggleMarkAll());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkAll_SecondPressForTheSameTextClears()
    {
        TextEditorControl editor = NewEditor("ab ab");
        editor.LastFind = "ab";
        editor.ToggleMarkAll();

        Assert.AreEqual(0, editor.ToggleMarkAll());
        Assert.AreEqual(0, editor.Decorations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MarkAll_IsClearedByAnEdit()
    {
        TextEditorControl editor = NewEditor("ab ab");
        editor.LastFind = "ab";
        editor.ToggleMarkAll();

        await Press(editor, ConsoleKey.A, 'z');

        Assert.AreEqual(0, editor.Decorations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkAll_WithNoSearchAndNoSelection_MarksNothing()
    {
        TextEditorControl editor = NewEditor("ab");

        Assert.AreEqual(0, editor.ToggleMarkAll());
        Assert.AreEqual(0, editor.Decorations.Count);
    }

    // ---- Keys ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task LineCommandKeys_RunTheirCommands()
    {
        TextEditorControl editor = NewEditor("hello");
        await Select(editor, ConsoleKey.RightArrow, 5);
        await Press(editor, ConsoleKey.U, shift: true, control: true);
        Assert.AreEqual("HELLO", Text(editor));

        await Press(editor, ConsoleKey.J, shift: true, control: true);
        Assert.AreEqual("hello", Text(editor));

        await Press(editor, ConsoleKey.E, shift: true, control: true);
        Assert.AreEqual("\r", editor.TextModel.LineEnding);

        // Without a selection, mark all searches for the last find text.
        await Press(editor, ConsoleKey.RightArrow);
        editor.LastFind = "l";
        await Press(editor, ConsoleKey.H, shift: true, control: true);
        Assert.AreEqual(2, editor.Decorations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LineCommandKeys_SortWithSAndD()
    {
        TextEditorControl editor = NewEditor("b", "c", "a");
        await Press(editor, ConsoleKey.S, shift: true, control: true);
        Assert.AreEqual("a|b|c", Text(editor));

        await Press(editor, ConsoleKey.D, shift: true, control: true);
        Assert.AreEqual("c|b|a", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LineCommandKeys_AreConsumedOnAReadOnlyEditor()
    {
        TextEditorControl editor = NewEditor("hello");
        editor.IsReadOnly = true;
        await Select(editor, ConsoleKey.RightArrow, 5);

        Assert.IsTrue(await editor.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.U, true, false, true)));
        Assert.AreEqual("hello", Text(editor));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Hints_OfferTheLineCommandsOnlyWhenEditable()
    {
        TextEditorControl editor = NewEditor("x");
        Assert.IsTrue(editor.GetKeyHints().Any(h => h.Key == "Ctrl+Shift+U / Ctrl+Shift+J"));
        Assert.IsTrue(editor.GetKeyHints().Any(h => h.Key == "Ctrl+Shift+H"));

        editor.IsReadOnly = true;
        Assert.IsFalse(editor.GetKeyHints().Any(h => h.Key == "Ctrl+Shift+U / Ctrl+Shift+J"));
        Assert.IsTrue(editor.GetKeyHints().Any(h => h.Key == "Ctrl+Shift+H"));
    }

    // ---- Reloading ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReplaceAllText_PutsTheNewTextInAsSavedFileWithNoHistory()
    {
        TextEditorControl editor = NewEditor("old");
        await Press(editor, ConsoleKey.A, 'x');
        Assert.IsTrue(editor.CanUndo);

        editor.ReplaceAllText(["new", "text"], "\r\n", System.Text.Encoding.UTF8);

        Assert.AreEqual("new|text", Text(editor));
        Assert.AreEqual("\r\n", editor.TextModel.LineEnding);
        Assert.IsFalse(editor.IsDirty);
        Assert.IsFalse(editor.CanUndo);
    }
}
