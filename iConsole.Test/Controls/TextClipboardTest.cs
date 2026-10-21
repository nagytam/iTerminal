using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Copy, cut, paste and select all in the one-line box and the multi-line editor.</summary>
[TestClass]
public class TextClipboardTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Chord(ConsoleKey key, bool control = false, bool shift = false)
        => new('\0', key, shift, false, control);

    private sealed class BrokenClipboard : ITextClipboardService
    {
        public Task SetTextAsync(string text, CancellationToken cancellationToken = default) => throw new IOException("busy");

        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => throw new IOException("busy");
    }

    // ---- ClipboardKeys ----

    [TestMethod]
    public void ClipboardKeys_MapTheChordsAndTheOlderKeys()
    {
        Assert.AreEqual(ClipboardKeyAction.Copy, ClipboardKeys.ActionFor(Chord(ConsoleKey.C, control: true)));
        Assert.AreEqual(ClipboardKeyAction.Copy, ClipboardKeys.ActionFor(Chord(ConsoleKey.Insert, control: true)));
        Assert.AreEqual(ClipboardKeyAction.Cut, ClipboardKeys.ActionFor(Chord(ConsoleKey.X, control: true)));
        Assert.AreEqual(ClipboardKeyAction.Cut, ClipboardKeys.ActionFor(Chord(ConsoleKey.Delete, shift: true)));
        Assert.AreEqual(ClipboardKeyAction.Paste, ClipboardKeys.ActionFor(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual(ClipboardKeyAction.Paste, ClipboardKeys.ActionFor(Chord(ConsoleKey.Insert, shift: true)));
        Assert.AreEqual(ClipboardKeyAction.SelectAll, ClipboardKeys.ActionFor(Chord(ConsoleKey.A, control: true)));
    }

    [TestMethod]
    public void ClipboardKeys_IgnoreEverythingElse()
    {
        Assert.AreEqual(ClipboardKeyAction.None, ClipboardKeys.ActionFor(Chord(ConsoleKey.C)));
        Assert.AreEqual(ClipboardKeyAction.None, ClipboardKeys.ActionFor(Chord(ConsoleKey.C, control: true, shift: true)));
        Assert.AreEqual(ClipboardKeyAction.None, ClipboardKeys.ActionFor(Chord(ConsoleKey.Delete)));
        Assert.AreEqual(ClipboardKeyAction.None, ClipboardKeys.ActionFor(new KeyEvent('\0', ConsoleKey.V, false, true, true)));
        Assert.AreEqual(ClipboardKeyAction.None, ClipboardKeys.ActionFor(Chord(ConsoleKey.Z, control: true)));
    }

    [TestMethod]
    public async Task ClipboardKeys_ABrokenClipboardIsNotAnError()
    {
        Assert.IsFalse(await ClipboardKeys.TrySetAsync(new BrokenClipboard(), "x"));
        Assert.IsNull(await ClipboardKeys.TryGetAsync(new BrokenClipboard()));
        InProcessTextClipboardService working = new();
        Assert.IsTrue(await ClipboardKeys.TrySetAsync(working, "x"));
        Assert.AreEqual("x", await ClipboardKeys.TryGetAsync(working));
        Assert.AreEqual("a\nb\nc", ClipboardKeys.NormalizeLineBreaks("a\r\nb\rc"));
    }

    [TestMethod]
    public async Task TheAmbientClipboard_IsInProcessUntilTheApplicationReplacesIt()
    {
        ITextClipboardService original = TextClipboard.Current;
        try
        {
            InProcessTextClipboardService replacement = new();
            TextClipboard.Current = replacement;
            Assert.AreSame(replacement, TextClipboard.Current);
            Assert.ThrowsExactly<ArgumentNullException>(() => TextClipboard.Current = null!);
            Assert.IsNull(await new InProcessTextClipboardService().GetTextAsync());
        }
        finally
        {
            TextClipboard.Current = original;
        }
    }

    // ---- TextBox ----

    private static (TextBox Box, InProcessTextClipboardService Clipboard) NewBox(string text, long cursor = 0, bool readOnly = false)
    {
        InProcessTextClipboardService clipboard = new();
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 1),
            IsReadOnly = readOnly,
            Clipboard = clipboard,
        };
        root.AddControl(box);
        box.OnInitializeLayout();
        box.Text = text;
        box.SetFocus();
        box.ScrollableModel.CursorPosition.X = cursor;
        return (box, clipboard);
    }

    [TestMethod]
    public async Task TextBox_SelectAll_SelectsTheWholeValue()
    {
        (TextBox box, _) = NewBox("hello world", 3);
        Assert.IsTrue(await box.HandleKeyEventAsync(Chord(ConsoleKey.A, control: true)));
        Assert.AreEqual("hello world", box.SelectedText);
        Assert.AreEqual(11L, box.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task TextBox_SelectAll_OnAnEmptyBoxIsLeftAlone()
    {
        (TextBox box, _) = NewBox(string.Empty);
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.A, control: true)));
    }

    [TestMethod]
    public async Task TextBox_CopyPutsTheSelectionOnTheClipboard_AndKeepsIt()
    {
        (TextBox box, InProcessTextClipboardService clipboard) = NewBox("hello world");
        await box.HandleKeyEventAsync(Chord(ConsoleKey.RightArrow, shift: true));
        await box.HandleKeyEventAsync(Chord(ConsoleKey.RightArrow, shift: true));
        Assert.IsTrue(await box.HandleKeyEventAsync(Chord(ConsoleKey.C, control: true)));
        Assert.AreEqual("he", await clipboard.GetTextAsync());
        Assert.AreEqual("hello world", box.Text);
        Assert.IsTrue(box.HasSelection);
    }

    [TestMethod]
    public async Task TextBox_CopyWithoutASelection_IsLeftToWhoeverElseWantsTheKey()
    {
        (TextBox box, InProcessTextClipboardService clipboard) = NewBox("hello");
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.C, control: true)));
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.X, control: true)));
        Assert.IsNull(await clipboard.GetTextAsync());
    }

    [TestMethod]
    public async Task TextBox_CutRemovesTheSelectionAndCopiesIt()
    {
        (TextBox box, InProcessTextClipboardService clipboard) = NewBox("hello world");
        box.SelectAll();
        Assert.IsTrue(await box.HandleKeyEventAsync(Chord(ConsoleKey.Delete, shift: true)));
        Assert.AreEqual("hello world", await clipboard.GetTextAsync());
        Assert.AreEqual(string.Empty, box.Text);
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public async Task TextBox_PasteInsertsAtTheCursor_AndReplacesASelection()
    {
        (TextBox box, InProcessTextClipboardService clipboard) = NewBox("hello world", 5);
        await clipboard.SetTextAsync(",");
        Assert.IsTrue(await box.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual("hello, world", box.Text);
        Assert.AreEqual(6L, box.ScrollableModel.CursorPosition.X);

        box.SelectAll();
        await clipboard.SetTextAsync("bye");
        await box.HandleKeyEventAsync(Chord(ConsoleKey.Insert, shift: true));
        Assert.AreEqual("bye", box.Text);
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public async Task TextBox_PasteMakesOneLine_DropsWhatTheBoxDoesNotTake_AndStopsAtMaxLength()
    {
        (TextBox box, InProcessTextClipboardService clipboard) = NewBox(string.Empty);
        await clipboard.SetTextAsync("one\r\ntwo\tthree");
        await box.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true));
        Assert.AreEqual("one twothree", box.Text);
        Assert.IsFalse(box.Text.Contains('\n'));

        (TextBox shortBox, InProcessTextClipboardService other) = NewBox("ab");
        shortBox.MaxLength = 5;
        await other.SetTextAsync("123456789");
        await shortBox.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true));
        Assert.AreEqual(5, shortBox.TextLength);
    }

    [TestMethod]
    public async Task TextBox_PasteWithNothingOnTheClipboard_IsLeftAlone()
    {
        (TextBox box, _) = NewBox("hello");
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual("hello", box.Text);
        box.Clipboard = new BrokenClipboard();
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
    }

    [TestMethod]
    public async Task TextBox_ReadOnly_CopiesButDoesNotCutOrPaste()
    {
        (TextBox box, InProcessTextClipboardService clipboard) = NewBox("hello", readOnly: true);
        box.SelectAll();
        await box.HandleKeyEventAsync(Chord(ConsoleKey.X, control: true));
        Assert.AreEqual("hello", await clipboard.GetTextAsync());
        Assert.AreEqual("hello", box.Text);
        await clipboard.SetTextAsync("zzz");
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual("hello", box.Text);
    }

    [TestMethod]
    public async Task TextBox_KeysAreIgnoredWithoutFocus()
    {
        (TextBox box, InProcessTextClipboardService clipboard) = NewBox("hello");
        box.ResetFocus();
        await clipboard.SetTextAsync("zzz");
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual("hello", box.Text);
    }

    [TestMethod]
    public async Task PasswordBox_NeverCopiesOrCutsItsValue_ButTakesAPaste()
    {
        InProcessTextClipboardService clipboard = new();
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        PasswordBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(30, 1),
            Clipboard = clipboard,
        };
        root.AddControl(box);
        box.OnInitializeLayout();
        box.Text = "secret";
        box.SetFocus();
        box.SelectAll();
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.C, control: true)));
        Assert.IsFalse(await box.HandleKeyEventAsync(Chord(ConsoleKey.X, control: true)));
        Assert.IsNull(await clipboard.GetTextAsync());
        Assert.AreEqual("secret", box.Text);

        await clipboard.SetTextAsync("hunter2");
        Assert.IsTrue(await box.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual("hunter2", box.Text);
    }

    // ---- Editor ----

    private static (TextEditorControl Editor, InProcessTextClipboardService Clipboard) NewEditor(string[] lines, long x = 0, long y = 0, bool readOnly = false)
    {
        InProcessTextClipboardService clipboard = new();
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 8), IsReadOnly = readOnly, Clipboard = clipboard };
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
        return (editor, clipboard);
    }

    private static string Text(TextEditorControl editor) => string.Join("|", editor.TextModel.Lines);

    private static readonly string[] Poem = ["roses are red", "violets are blue", "sugar is sweet"];

    [TestMethod]
    public async Task Editor_SelectAll_SelectsEveryLine()
    {
        (TextEditorControl editor, _) = NewEditor(Poem, 3, 1);
        Assert.IsTrue(await editor.HandleKeyEventAsync(Chord(ConsoleKey.A, control: true)));
        Assert.AreEqual("roses are red\nviolets are blue\nsugar is sweet", editor.SelectedText);
        Assert.AreEqual(2L, editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task Editor_SelectAll_OnAnEmptyDocumentIsLeftAlone()
    {
        (TextEditorControl empty, _) = NewEditor([]);
        Assert.IsFalse(await empty.HandleKeyEventAsync(Chord(ConsoleKey.A, control: true)));
        (TextEditorControl blank, _) = NewEditor([string.Empty, string.Empty]);
        Assert.IsFalse(await blank.HandleKeyEventAsync(Chord(ConsoleKey.A, control: true)));
    }

    [TestMethod]
    public async Task Editor_CopyAndCut_UseTheSelection()
    {
        (TextEditorControl editor, InProcessTextClipboardService clipboard) = NewEditor(Poem, 6, 0);
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.DownArrow, shift: true));
        Assert.IsTrue(await editor.HandleKeyEventAsync(Chord(ConsoleKey.Insert, control: true)));
        Assert.AreEqual("are red\nviolet", await clipboard.GetTextAsync());
        Assert.AreEqual("roses are red|violets are blue|sugar is sweet", Text(editor));

        Assert.IsTrue(await editor.HandleKeyEventAsync(Chord(ConsoleKey.X, control: true)));
        Assert.AreEqual("roses s are blue|sugar is sweet", Text(editor));
        Assert.IsTrue(editor.IsDirty);
        Assert.IsFalse(editor.HasSelection);
    }

    [TestMethod]
    public async Task Editor_CopyWithoutASelection_IsLeftToWhoeverElseWantsTheKey()
    {
        (TextEditorControl editor, InProcessTextClipboardService clipboard) = NewEditor(Poem);
        Assert.IsFalse(await editor.HandleKeyEventAsync(Chord(ConsoleKey.C, control: true)));
        Assert.IsNull(await clipboard.GetTextAsync());
    }

    [TestMethod]
    public async Task Editor_PasteOneLine_InsertsAtTheCursor()
    {
        (TextEditorControl editor, InProcessTextClipboardService clipboard) = NewEditor(Poem, 6, 0);
        await clipboard.SetTextAsync("very ");
        Assert.IsTrue(await editor.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual("roses very are red|violets are blue|sugar is sweet", Text(editor));
        Assert.AreEqual(11L, editor.ScrollableModel.CursorPosition.X);
        Assert.IsTrue(editor.IsDirty);
    }

    [TestMethod]
    public async Task Editor_PasteSeveralLines_SplitsTheLineAroundThem()
    {
        (TextEditorControl editor, InProcessTextClipboardService clipboard) = NewEditor(Poem, 6, 0);
        await clipboard.SetTextAsync("X\r\nmiddle\r\nY");
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.Insert, shift: true));
        Assert.AreEqual("roses X|middle|Yare red|violets are blue|sugar is sweet", Text(editor));
        Assert.AreEqual(1L, editor.ScrollableModel.CursorPosition.X);
        Assert.AreEqual(2L, editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task Editor_Paste_ReplacesASelection_AndIntoAnEmptyDocument()
    {
        (TextEditorControl editor, InProcessTextClipboardService clipboard) = NewEditor(Poem);
        editor.SelectAll();
        await clipboard.SetTextAsync("one\ntwo");
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true));
        Assert.AreEqual("one|two", Text(editor));

        (TextEditorControl empty, InProcessTextClipboardService other) = NewEditor([]);
        await other.SetTextAsync("a\nb");
        await empty.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true));
        Assert.AreEqual("a|b", Text(empty));
    }

    [TestMethod]
    public async Task Editor_Paste_DropsControlCharactersButKeepsTabs()
    {
        (TextEditorControl editor, InProcessTextClipboardService clipboard) = NewEditor([string.Empty]);
        await clipboard.SetTextAsync("a\tb\u0007c");
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true));
        Assert.AreEqual("a\tbc", Text(editor));
    }

    [TestMethod]
    public async Task Editor_ReadOnly_CopiesButDoesNotCutOrPaste()
    {
        (TextEditorControl editor, InProcessTextClipboardService clipboard) = NewEditor(Poem, readOnly: true);
        editor.SelectAll();
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.X, control: true));
        Assert.AreEqual("roses are red\nviolets are blue\nsugar is sweet", await clipboard.GetTextAsync());
        Assert.AreEqual("roses are red|violets are blue|sugar is sweet", Text(editor));
        await clipboard.SetTextAsync("zzz");
        Assert.IsFalse(await editor.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.IsFalse(editor.IsDirty);
    }

    [TestMethod]
    public async Task Editor_PasteFromABrokenOrEmptyClipboard_IsLeftAlone()
    {
        (TextEditorControl editor, _) = NewEditor(Poem);
        Assert.IsFalse(await editor.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        editor.Clipboard = new BrokenClipboard();
        Assert.IsFalse(await editor.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true)));
        Assert.AreEqual("roses are red|violets are blue|sugar is sweet", Text(editor));
    }

    [TestMethod]
    public async Task Editor_CopyAndPasteRoundTripsAMultiLineSelection()
    {
        (TextEditorControl editor, _) = NewEditor(Poem, 0, 0);
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.DownArrow, shift: true, control: false));
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.C, control: true));
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.DownArrow));
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.End));
        await editor.HandleKeyEventAsync(Chord(ConsoleKey.V, control: true));
        Assert.AreEqual("roses are red|violets are blue|sugar is sweetroses are red|", Text(editor));
    }
}
