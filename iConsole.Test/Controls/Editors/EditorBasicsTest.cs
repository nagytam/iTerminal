using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Editors;

/// <summary>
/// What an editor is expected to do before anything else: indent, keep the indent, save the file as it was read, ask
/// before throwing changes away; and what the viewer must keep doing — colour its text, wrapped or not.
/// </summary>
[TestClass]
public class EditorBasicsTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextEditorControl NewEditor(params string[] lines)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 8) };
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

    private static Task Press(TextEditorControl editor, ConsoleKey key, char c = '\0', bool shift = false, bool control = false)
        => editor.HandleKeyEventAsync(new KeyEvent(c, key, shift, false, control));

    private static async Task Type(TextEditorControl editor, string text)
    {
        foreach (char c in text)
        {
            await Press(editor, ConsoleKey.A, c);
        }
    }

    private static string Text(TextEditorControl editor) => string.Join("|", editor.TextModel.Lines);

    // ---- Tab ----

    [TestMethod]
    public async Task Tab_PutsSpacesUpToTheNextStop()
    {
        TextEditorControl editor = NewEditor("ab");
        editor.ScrollableModel.CursorPosition.X = 2;

        await Press(editor, ConsoleKey.Tab, '\t');

        Assert.AreEqual("ab  |".TrimEnd('|'), Text(editor));
        Assert.AreEqual(4, editor.ScrollableModel.CursorPosition.X);
        Assert.IsTrue(editor.IsDirty);
    }

    [TestMethod]
    public async Task Tab_OnAnEmptyDocumentStartsALine()
    {
        TextEditorControl editor = NewEditor();

        await Press(editor, ConsoleKey.Tab, '\t');

        Assert.AreEqual("    ", Text(editor));
    }

    [TestMethod]
    public async Task ShiftTab_TakesOneStepOffTheLineAndKeepsTheCursorOnItsText()
    {
        TextEditorControl editor = NewEditor("      code");
        editor.ScrollableModel.CursorPosition.X = 8;

        await Press(editor, ConsoleKey.Tab, '\t', shift: true);

        Assert.AreEqual("  code", Text(editor));
        Assert.AreEqual(4, editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task ShiftTab_RemovesATabCharacterToo_AndNothingFromAFlushLine()
    {
        TextEditorControl editor = NewEditor("\tx", "y");

        await Press(editor, ConsoleKey.Tab, '\t', shift: true);
        Assert.AreEqual("x|y", Text(editor));

        editor.ScrollableModel.CursorPosition.Y = 1;
        await Press(editor, ConsoleKey.Tab, '\t', shift: true);
        Assert.AreEqual("x|y", Text(editor));
    }

    [TestMethod]
    public async Task Tab_WithASelectionIndentsEveryLineItTouchesAndKeepsThemSelected()
    {
        TextEditorControl editor = NewEditor("one", "two", "three", "four");
        editor.ScrollableModel.CursorPosition.Y = 0;
        editor.ScrollableModel.CursorPosition.X = 1;
        await Press(editor, ConsoleKey.DownArrow, shift: true);
        await Press(editor, ConsoleKey.DownArrow, shift: true);
        Assert.IsTrue(editor.HasSelection);

        await Press(editor, ConsoleKey.Tab, '\t');

        Assert.AreEqual("    one|    two|    three|four", Text(editor));
        Assert.IsTrue(editor.HasSelection);

        await Press(editor, ConsoleKey.Tab, '\t', shift: true);
        Assert.AreEqual("one|two|three|four", Text(editor));
    }

    [TestMethod]
    public async Task Tab_ASelectionEndingAtTheStartOfALineLeavesThatLineAlone()
    {
        TextEditorControl editor = NewEditor("one", "two", "three");
        await Press(editor, ConsoleKey.DownArrow, shift: true);
        await Press(editor, ConsoleKey.DownArrow, shift: true);
        editor.ScrollableModel.CursorPosition.X = 0;

        await Press(editor, ConsoleKey.Tab, '\t');

        Assert.AreEqual("    one|    two|three", Text(editor));
    }

    [TestMethod]
    public async Task Tab_IsOneStepOfUndo()
    {
        TextEditorControl editor = NewEditor("a", "b");
        await Press(editor, ConsoleKey.DownArrow, shift: true);
        await Press(editor, ConsoleKey.End, shift: true);
        await Press(editor, ConsoleKey.Tab, '\t');
        Assert.AreEqual("    a|    b", Text(editor));

        await Press(editor, ConsoleKey.Z, control: true);

        Assert.AreEqual("a|b", Text(editor));
    }

    [TestMethod]
    public async Task Tab_ChangesNothingInAReadOnlyEditor()
    {
        TextEditorControl editor = NewEditor("a");
        editor.IsReadOnly = true;

        await Press(editor, ConsoleKey.Tab, '\t');

        Assert.AreEqual("a", Text(editor));
        Assert.IsFalse(editor.IsDirty);
    }

    // ---- Auto-indent ----

    [TestMethod]
    public async Task Enter_StartsTheNewLineWithTheIndentOfTheOldOne()
    {
        TextEditorControl editor = NewEditor("    if (x)");
        editor.ScrollableModel.CursorPosition.X = 10;

        await Press(editor, ConsoleKey.Enter, '\r');

        Assert.AreEqual("    if (x)|    ", Text(editor));
        Assert.AreEqual(1, editor.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual(4, editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task Enter_InTheMiddleOfTheIndentCopiesOnlyWhatIsBeforeTheCursor()
    {
        TextEditorControl editor = NewEditor("      x");
        editor.ScrollableModel.CursorPosition.X = 2;

        await Press(editor, ConsoleKey.Enter, '\r');

        Assert.AreEqual("  |      x", Text(editor), "two spaces stay, and the four after the cursor follow the two copied");
        Assert.AreEqual(2, editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task Enter_OnAFlushLineStillStartsAtTheLeftEdge()
    {
        TextEditorControl editor = NewEditor("abc");
        editor.ScrollableModel.CursorPosition.X = 3;

        await Press(editor, ConsoleKey.Enter, '\r');

        Assert.AreEqual("abc|", Text(editor));
        Assert.AreEqual(0, editor.ScrollableModel.CursorPosition.X);
    }

    // ---- Line endings ----

    [TestMethod]
    [DataRow("a\r\nb\r\nc", "\r\n")]
    [DataRow("a\nb\nc", "\n")]
    [DataRow("a\rb\rc", "\r")]
    [DataRow("a\r\nb\nc\nd", "\n")]
    [DataRow("a\r\nb\r\nc\nd", "\r\n")]
    public void Detect_FindsTheBreakTheTextUsesMost(string text, string expected)
    {
        Assert.AreEqual(expected, LineEndings.Detect(text));
    }

    [TestMethod]
    public void Detect_NoBreakAtAllGivesTheSystemsOwn()
    {
        Assert.AreEqual(Environment.NewLine, LineEndings.Detect("one line"));
        Assert.AreEqual(Environment.NewLine, LineEndings.Detect(string.Empty));
    }

    [TestMethod]
    public void Split_ComesApartAtEveryKindOfBreak()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "c", "d", "" }, LineEndings.Split("a\r\nb\nc\rd\n"));
    }

    [TestMethod]
    public async Task ReadingThenSaving_KeepsTheFilesOwnLineBreakAndTrailingNewline()
    {
        foreach (string eol in new[] { "\n", "\r\n" })
        {
            string original = $"first{eol}second{eol}third{eol}";
            Url url = new("file://C:/x/note.txt");
            Mock<IMemoryFile> file = new();
            file.SetupGet(f => f.Content).Returns(System.Text.Encoding.UTF8.GetBytes(original));
            byte[]? written = null;
            Mock<IContentModel> model = new();
            model.Setup(m => m.ExistsFile(url)).Returns(true);
            model.Setup(m => m.ReadFileAsync(url)).ReturnsAsync(file.Object);
            model.Setup(m => m.WriteFileAsync(url, It.IsAny<MemoryStream>()))
                .Callback<Url, MemoryStream>((_, s) => written = s.ToArray())
                .Returns(Task.CompletedTask);
            Mock<IContentModelService> models = new();
            models.Setup(m => m.GetFocusedModel()).Returns(model.Object);
            models.Setup(m => m.GetLastContentModel()).Returns(model.Object);
            TextFileEditorDialog dialog = new(models.Object, new FileToTextConverterService(), Mock.Of<IBeepService>(),
                Mock.Of<IDateTimeProviderService>(), new ThemeService()) { Url = url };

            await dialog.OnInitializeAsync();
            dialog.TextEditorControl.TextModel.Lines[0] = "FIRST";
            dialog.TextEditorControl.IsDirty = true;
            Assert.IsTrue(await dialog.SaveAsync());

            Assert.AreEqual(eol, dialog.TextEditorControl.TextModel.LineEnding);
            Assert.AreEqual($"FIRST{eol}second{eol}third{eol}", System.Text.Encoding.UTF8.GetString(written!));
            Assert.IsFalse(dialog.TextEditorControl.IsDirty);
        }
    }

    // ---- Closing a changed file ----

    private sealed record Rig(TextFileEditorDialog Dialog, Control Root, Mock<IContentModel> Model, Func<byte[]?> Written);

    private static async Task<Rig> OpenAsync(string content = "one\ntwo\n")
    {
        Url url = new("file://C:/x/note.txt");
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(System.Text.Encoding.UTF8.GetBytes(content));
        byte[]? written = null;
        Mock<IContentModel> model = new();
        model.Setup(m => m.ExistsFile(url)).Returns(true);
        model.Setup(m => m.ReadFileAsync(url)).ReturnsAsync(file.Object);
        model.Setup(m => m.WriteFileAsync(url, It.IsAny<MemoryStream>()))
            .Callback<Url, MemoryStream>((_, s) => written = s.ToArray())
            .Returns(Task.CompletedTask);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(model.Object);
        models.Setup(m => m.GetLastContentModel()).Returns(model.Object);
        TextFileEditorDialog dialog = new(models.Object, new FileToTextConverterService(), Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(), Theme()) { Url = url };
        Control root = new Window(Theme()) { ViewSize = new Size(100, 30) };
        await root.ShowDialogAsync(dialog);
        return new Rig(dialog, root, model, () => written);
    }

    private static UnsavedChangesDialog? Prompt(Control root) => root.Controls.OfType<UnsavedChangesDialog>().SingleOrDefault();

    [TestMethod]
    public async Task EscOnAnUnchangedFile_ClosesAtOnce()
    {
        Rig rig = await OpenAsync();

        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.IsNull(Prompt(rig.Root));
        Assert.IsFalse(rig.Root.Controls.Contains(rig.Dialog));
    }

    [TestMethod]
    public async Task EscOnAChangedFile_AsksFirstAndStaysOpen()
    {
        Rig rig = await OpenAsync();
        rig.Dialog.TextEditorControl.IsDirty = true;

        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.IsNotNull(Prompt(rig.Root), "the question is on screen");
        Assert.IsTrue(rig.Root.Controls.Contains(rig.Dialog), "and the editor is still there");
        Assert.IsFalse(rig.Dialog.PendingClose.IsCompleted);
    }

    [TestMethod]
    public async Task Discard_ClosesAndLosesTheChanges()
    {
        Rig rig = await OpenAsync();
        rig.Dialog.TextEditorControl.IsDirty = true;
        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        await Prompt(rig.Root)!.HandleKeyEventAsync(new KeyEvent('d', ConsoleKey.D, false, false, false));
        await rig.Dialog.PendingClose;

        Assert.IsFalse(rig.Root.Controls.Contains(rig.Dialog));
        Assert.IsNull(rig.Written(), "nothing was written");
    }

    [TestMethod]
    public async Task Save_WritesTheFileAndThenCloses()
    {
        Rig rig = await OpenAsync("one\ntwo\n");
        rig.Dialog.TextEditorControl.TextModel.Lines[0] = "ONE";
        rig.Dialog.TextEditorControl.IsDirty = true;
        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        await Prompt(rig.Root)!.HandleKeyEventAsync(new KeyEvent('s', ConsoleKey.S, false, false, false));
        await rig.Dialog.PendingClose;

        Assert.AreEqual("ONE\ntwo\n", System.Text.Encoding.UTF8.GetString(rig.Written()!));
        Assert.IsFalse(rig.Root.Controls.Contains(rig.Dialog));
    }

    [TestMethod]
    public async Task EscOnTheQuestion_IsCancel_AndTheEditorStaysWithItsText()
    {
        Rig rig = await OpenAsync();
        rig.Dialog.TextEditorControl.IsDirty = true;
        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        await Prompt(rig.Root)!.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));
        await rig.Dialog.PendingClose;

        Assert.IsNull(Prompt(rig.Root));
        Assert.IsTrue(rig.Root.Controls.Contains(rig.Dialog));
        Assert.IsTrue(rig.Dialog.TextEditorControl.IsDirty);
        Assert.IsNull(rig.Written());
    }

    [TestMethod]
    public async Task ASaveThatFailsKeepsTheEditorOpen()
    {
        Rig rig = await OpenAsync();
        rig.Model.Setup(m => m.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>())).ThrowsAsync(new IOException("disk full"));
        rig.Dialog.TextEditorControl.IsDirty = true;
        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        await Prompt(rig.Root)!.HandleKeyEventAsync(new KeyEvent('s', ConsoleKey.S, false, false, false));
        await rig.Dialog.PendingClose;

        Assert.IsTrue(rig.Root.Controls.Contains(rig.Dialog));
        Assert.IsTrue(rig.Dialog.TextEditorControl.IsDirty);
        Assert.IsTrue(rig.Dialog.Title.Contains("disk full", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CtrlS_SavesLikeF2()
    {
        Rig rig = await OpenAsync("one\n");
        rig.Dialog.TextEditorControl.TextModel.Lines[0] = "uno";
        rig.Dialog.TextEditorControl.IsDirty = true;

        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('s', ConsoleKey.S, false, false, true));

        Assert.AreEqual("uno\n", System.Text.Encoding.UTF8.GetString(rig.Written()!));
        Assert.IsFalse(rig.Dialog.TextEditorControl.IsDirty);
    }

    [TestMethod]
    public async Task TabInTheDialogIndentsInsteadOfMovingFocus()
    {
        Rig rig = await OpenAsync("one\n");

        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\t', ConsoleKey.Tab, false, false, false));

        Assert.AreEqual("    one", rig.Dialog.TextEditorControl.TextModel.Lines[0]);
    }

    [TestMethod]
    public async Task F5_InTheEditorAsksForALineToGoTo()
    {
        Rig rig = await OpenAsync(string.Join("\n", Enumerable.Range(1, 50).Select(i => "line " + i)));

        await rig.Dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F5, false, false, false));

        Assert.IsTrue(rig.Root.Controls.OfType<GoToLineDialog>().Any() || rig.Dialog.Controls.OfType<GoToLineDialog>().Any()
            || FindGoToLine(rig.Root), "the go-to-line question is up");
    }

    private static bool FindGoToLine(IControl control) =>
        control.Controls.Any(c => c is GoToLineDialog || FindGoToLine(c));

    // ---- Viewer syntax ----

    [TestMethod]
    public void Syntax_PicksALanguageByFileNameWhenTheNameIsAllThereIs()
    {
        Assert.AreEqual("Build file", SyntaxHighlighter.ForFile("/src/app/Dockerfile")?.Name);
        Assert.AreEqual("Build file", SyntaxHighlighter.ForFile(@"C:\src\Makefile")?.Name);
        Assert.AreEqual("Shell", SyntaxHighlighter.ForFile("/home/a/.bashrc")?.Name);
        Assert.AreEqual("Config", SyntaxHighlighter.ForFile("/repo/.gitignore")?.Name);
        Assert.AreEqual("Lua", SyntaxHighlighter.ForFile("init.lua")?.Name);
        Assert.AreEqual("Visual Basic", SyntaxHighlighter.ForFile("Form1.vb")?.Name);
        Assert.AreEqual("F#", SyntaxHighlighter.ForFile("Program.fs")?.Name);
        Assert.IsNull(SyntaxHighlighter.ForFile("/notes/readme.txt"));
    }

    [TestMethod]
    public void Syntax_LuaBlockCommentsAndLineCommentsAreTold()
    {
        SyntaxLanguage lua = SyntaxHighlighter.ForFile("a.lua")!;
        List<SyntaxSpan> spans = [];
        Assert.IsTrue(SyntaxHighlighter.Tokenize(lua, "local x = 1 --[[ begins", false, spans));
        Assert.IsTrue(spans.Any(s => s.Kind == SyntaxKind.Keyword));
        spans.Clear();
        Assert.IsFalse(SyntaxHighlighter.Tokenize(lua, "ends ]] return -- tail", true, spans));
        Assert.AreEqual(2, spans.Count(s => s.Kind == SyntaxKind.Comment));
    }

    private static TextViewerControl NewViewer(bool wrap, params string[] lines)
    {
        IThemeService theme = Theme();
        Window parent = new(theme) { ViewSize = new Size(30, 12) };
        TextViewerControl viewer = new(false, theme) { Parent = parent, Position = new Position(0, 0), ViewSize = new Size(30, 12) };
        foreach (string line in lines)
        {
            viewer.TextModel.Lines.Add(line);
        }

        viewer.Syntax = SyntaxHighlighter.ForFile("a.cs");
        viewer.WrapLines = wrap;
        viewer.OnInitializeLayout();
        return viewer;
    }

    [TestMethod]
    public void Viewer_ColoursKeywordsWhenNotWrapping()
    {
        TextViewerControl viewer = NewViewer(wrap: false, "x", "return value");
        ScreenBuffer screen = new(30, 12);
        viewer.Draw(screen);

        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(0, 1));
        Assert.AreEqual(Color.White, screen.ForegroundAt(8, 1));
    }

    [TestMethod]
    public void Viewer_ColoursKeywordsWhenWrapping()
    {
        TextViewerControl viewer = NewViewer(wrap: true, "x", "return value");
        ScreenBuffer screen = new(30, 12);
        viewer.Draw(screen);

        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(0, 1), "'return' is a keyword in a wrapped row too");
        Assert.AreEqual(Color.White, screen.ForegroundAt(8, 1));
    }

    [TestMethod]
    public void Viewer_AKeywordOnTheSecondRowOfAWrappedLineIsColouredFromItsOwnPlace()
    {
        // 28 columns of text per row: the 28 characters of "a" padding fill the first row, "return" starts the second.
        string line = new string('a', 28) + "return";
        TextViewerControl viewer = NewViewer(wrap: true, "x", line);
        ScreenBuffer screen = new(30, 12);
        viewer.Draw(screen);

        Assert.AreEqual(Color.White, screen.ForegroundAt(0, 1), "the padding is a plain word");
        Assert.AreEqual(Color.White, screen.ForegroundAt(0, 2), "the identifier runs on into the second row");
    }

    [TestMethod]
    public void Viewer_ABlockCommentThatRunsOverWrappedLinesStaysAComment()
    {
        TextViewerControl viewer = NewViewer(wrap: true, "/* start", "return end", "*/ return");
        ScreenBuffer screen = new(30, 12);
        viewer.Draw(screen);

        Assert.AreEqual(Color.Gray, screen.ForegroundAt(0, 1), "the second line is inside the comment");
        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(3, 2), "and 'return' after the close is a keyword again");
    }

    [TestMethod]
    public void Viewer_TheCursorRowKeepsItsHighlightWhenWrapping()
    {
        TextViewerControl viewer = NewViewer(wrap: true, "return value");
        viewer.ScrollableModel.CursorPosition.Y = 0;
        ScreenBuffer screen = new(30, 12);
        viewer.Draw(screen);

        Assert.AreEqual(Color.Yellow, screen.BackgroundAt(0, 0));
        Assert.AreNotEqual(Color.Cyan, screen.ForegroundAt(0, 0));
    }
}
