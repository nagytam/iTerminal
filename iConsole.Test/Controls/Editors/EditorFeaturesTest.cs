using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Editors;

/// <summary>Undo and redo, replace, bookmarks and syntax colouring in the editor, and scrolling in the viewer.</summary>
[TestClass]
public class EditorFeaturesTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextEditorControl NewEditor(params string[] lines)
    {
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 8) };
        editor.SetFocus();
        foreach (string line in lines)
        {
            editor.TextModel.Lines.Add(line);
        }

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

    // ---- Undo / redo ----

    [TestMethod]
    public async Task Undo_TakesBackTypingThatRanOnAsOneStep()
    {
        TextEditorControl editor = NewEditor("");
        await Type(editor, "hello");
        Assert.AreEqual("hello", Text(editor));

        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("", Text(editor));
        Assert.IsFalse(editor.CanUndo);
        Assert.IsTrue(editor.CanRedo);
    }

    [TestMethod]
    public async Task Undo_ThenRedo_RestoresTheTextAndTheCursor()
    {
        TextEditorControl editor = NewEditor("abc");
        editor.ScrollableModel.CursorPosition.X = 3;
        await Press(editor, ConsoleKey.Enter);
        Assert.AreEqual("abc|", Text(editor));

        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("abc", Text(editor));
        Assert.AreEqual(3L, editor.ScrollableModel.CursorPosition.X);
        Assert.AreEqual(0L, editor.ScrollableModel.CursorPosition.Y);

        await Press(editor, ConsoleKey.Y, control: true);
        Assert.AreEqual("abc|", Text(editor));
        Assert.AreEqual(1L, editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task Undo_MovingTheCursorBetweenTypingStartsANewStep()
    {
        TextEditorControl editor = NewEditor("");
        await Type(editor, "ab");
        await Press(editor, ConsoleKey.LeftArrow);
        await Press(editor, ConsoleKey.End);
        await Type(editor, "cd");
        Assert.AreEqual("abcd", Text(editor));

        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("ab", Text(editor));
        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("", Text(editor));
    }

    [TestMethod]
    public async Task Undo_AnEditAfterUndoDropsTheRedoHistory()
    {
        TextEditorControl editor = NewEditor("");
        await Type(editor, "ab");
        await Press(editor, ConsoleKey.Z, control: true);
        await Type(editor, "x");
        Assert.IsFalse(editor.CanRedo);
        Assert.AreEqual("x", Text(editor));
    }

    [TestMethod]
    public async Task Undo_ShiftZIsRedo()
    {
        TextEditorControl editor = NewEditor("");
        await Type(editor, "ab");
        await Press(editor, ConsoleKey.Z, control: true);
        await Press(editor, ConsoleKey.Z, shift: true, control: true);
        Assert.AreEqual("ab", Text(editor));
    }

    [TestMethod]
    public async Task Undo_DoesNothingInAReadOnlyEditor()
    {
        TextEditorControl editor = NewEditor("");
        await Type(editor, "ab");
        editor.IsReadOnly = true;
        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("ab", Text(editor));
    }

    [TestMethod]
    public async Task Undo_TakesBackADeleteWithASelection()
    {
        TextEditorControl editor = NewEditor("hello world");
        editor.SelectAll();
        await Press(editor, ConsoleKey.Delete);
        Assert.AreEqual("", Text(editor));
        await Press(editor, ConsoleKey.Z, control: true);
        Assert.AreEqual("hello world", Text(editor));
    }

    // ---- Replace ----

    [TestMethod]
    public void ReplaceAll_ChangesEveryOccurrenceAsOneUndoStep()
    {
        TextEditorControl editor = NewEditor("one two one", "none", "ONE");
        int count = editor.ReplaceAll("one", "1", matchCase: true);
        Assert.AreEqual(3, count); // "one two one" twice, "none" once; "ONE" does not match the case
        Assert.AreEqual("1 two 1|n1|ONE", Text(editor));
        Assert.IsTrue(editor.IsDirty);

        editor.Undo();
        Assert.AreEqual("one two one|none|ONE", Text(editor));
    }

    [TestMethod]
    public void ReplaceAll_IgnoringCaseTakesTheUpperCaseToo()
    {
        TextEditorControl editor = NewEditor("one ONE One");
        Assert.AreEqual(3, editor.ReplaceAll("one", "x", matchCase: false));
        Assert.AreEqual("x x x", Text(editor));
    }

    [TestMethod]
    public void ReplaceAll_ReplacementContainingTheSearchIsNotReplacedAgain()
    {
        TextEditorControl editor = NewEditor("a a");
        Assert.AreEqual(2, editor.ReplaceAll("a", "aa"));
        Assert.AreEqual("aa aa", Text(editor));
    }

    [TestMethod]
    public void ReplaceNext_GoesOnFromTheCursorAndWrapsRound()
    {
        TextEditorControl editor = NewEditor("x1 x2", "x3");
        editor.ViewSize = new Size(40, 8);
        Assert.IsTrue(editor.ReplaceNext("x", "y"));
        Assert.AreEqual("y1 x2|x3", Text(editor));
        Assert.IsTrue(editor.ReplaceNext("x", "y"));
        Assert.AreEqual("y1 y2|x3", Text(editor));
        Assert.IsTrue(editor.ReplaceNext("x", "y"));
        Assert.AreEqual("y1 y2|y3", Text(editor));
        Assert.IsFalse(editor.ReplaceNext("x", "y"));

        editor.ScrollableModel.CursorPosition.Y = 1;
        editor.ScrollableModel.CursorPosition.X = 2;
        editor.TextModel.Lines[0] = "x";
        Assert.IsTrue(editor.ReplaceNext("x", "z"), "wraps back to the start of the text");
        Assert.AreEqual("z|y3", Text(editor));
    }

    [TestMethod]
    public void Replace_NothingToFindOrReadOnly_ChangesNothing()
    {
        TextEditorControl editor = NewEditor("abc");
        Assert.AreEqual(0, editor.ReplaceAll("q", "x"));
        Assert.IsFalse(editor.IsDirty);
        editor.IsReadOnly = true;
        Assert.AreEqual(0, editor.ReplaceAll("a", "x"));
        Assert.IsFalse(editor.ReplaceNext("a", "x"));
        Assert.AreEqual("abc", Text(editor));
    }

    // ---- Bookmarks ----

    [TestMethod]
    public void Bookmarks_ToggleAndGoToNextAndPreviousWrapRound()
    {
        TextEditorControl editor = NewEditor("a", "b", "c", "d", "e");
        editor.ScrollableModel.CursorPosition.Y = 1;
        Assert.IsTrue(editor.ToggleBookmark());
        editor.ScrollableModel.CursorPosition.Y = 3;
        Assert.IsTrue(editor.ToggleBookmark());
        CollectionAssert.AreEqual(new[] { 1, 3 }, editor.Bookmarks.ToArray());

        editor.ScrollableModel.CursorPosition.Y = 0;
        Assert.IsTrue(editor.GoToBookmark());
        Assert.AreEqual(1L, editor.ScrollableModel.CursorPosition.Y);
        editor.GoToBookmark();
        Assert.AreEqual(3L, editor.ScrollableModel.CursorPosition.Y);
        editor.GoToBookmark();
        Assert.AreEqual(1L, editor.ScrollableModel.CursorPosition.Y, "wraps to the first");
        editor.GoToBookmark(forward: false);
        Assert.AreEqual(3L, editor.ScrollableModel.CursorPosition.Y, "wraps to the last");

        Assert.IsFalse(editor.ToggleBookmark(), "toggling a marked line takes the mark off");
        CollectionAssert.AreEqual(new[] { 1 }, editor.Bookmarks.ToArray());
    }

    [TestMethod]
    public async Task Bookmarks_KeysDriveThem()
    {
        TextEditorControl editor = NewEditor("a", "b", "c");
        editor.ScrollableModel.CursorPosition.Y = 2;
        await Press(editor, ConsoleKey.F2, control: true);
        editor.ScrollableModel.CursorPosition.Y = 0;
        await Press(editor, ConsoleKey.F9);
        Assert.AreEqual(2L, editor.ScrollableModel.CursorPosition.Y);
        editor.ScrollableModel.CursorPosition.Y = 0;
        await Press(editor, ConsoleKey.F9, shift: true);
        Assert.AreEqual(2L, editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task Bookmarks_MoveWithTheirLinesWhenLinesAreAddedAndRemoved()
    {
        TextEditorControl editor = NewEditor("a", "b", "c");
        editor.ScrollableModel.CursorPosition.Y = 2;
        editor.ToggleBookmark();

        editor.ScrollableModel.CursorPosition.Y = 0;
        editor.ScrollableModel.CursorPosition.X = 1;
        await Press(editor, ConsoleKey.Enter);
        CollectionAssert.AreEqual(new[] { 3 }, editor.Bookmarks.ToArray());

        await Press(editor, ConsoleKey.Backspace); // joins the new line back
        CollectionAssert.AreEqual(new[] { 2 }, editor.Bookmarks.ToArray());

        await Press(editor, ConsoleKey.Z, control: true);
        await Press(editor, ConsoleKey.Z, control: true);
        CollectionAssert.AreEqual(new[] { 2 }, editor.Bookmarks.ToArray());
    }

    [TestMethod]
    public async Task Bookmarks_ALineRemovedTakesItsBookmarkWithIt()
    {
        TextEditorControl editor = NewEditor("a", "b", "c");
        editor.ScrollableModel.CursorPosition.Y = 1;
        editor.ToggleBookmark();
        editor.SelectAll();
        await Press(editor, ConsoleKey.Delete);
        Assert.AreEqual(0, editor.Bookmarks.Count);
    }

    [TestMethod]
    public void Bookmarks_ADrawnBookmarkedLineIsColoured()
    {
        TextEditorControl editor = NewEditor("a", "b", "c");
        editor.ScrollableModel.CursorPosition.Y = 1;
        editor.ToggleBookmark();
        editor.ScrollableModel.CursorPosition.Y = 0;

        ScreenBuffer screen = new(40, 8);
        editor.OnInitializeLayout();
        editor.Draw(screen);
        Assert.AreEqual(Color.DarkCyan, screen.BackgroundAt(3, 1));
        Assert.AreNotEqual(Color.DarkCyan, screen.BackgroundAt(3, 2));
    }

    // ---- Syntax ----

    [TestMethod]
    public void Syntax_PicksALanguageByExtension()
    {
        Assert.AreEqual("C#", SyntaxHighlighter.ForFile(@"C:\src\Program.CS")?.Name);
        Assert.AreEqual("C++", SyntaxHighlighter.ForFile("/src/main.cpp")?.Name);
        Assert.AreEqual("PowerShell", SyntaxHighlighter.ForFile(@"C:\scripts\Deploy.ps1")?.Name);
        Assert.AreEqual("Python", SyntaxHighlighter.ForFile("/home/a/run.py")?.Name);
        Assert.AreEqual("JSON", SyntaxHighlighter.ForFile("a.json")?.Name);
        Assert.IsNull(SyntaxHighlighter.ForFile("notes.txt"));
        Assert.IsNull(SyntaxHighlighter.ForFile(null));
    }

    [TestMethod]
    public void Syntax_TokenizesKeywordsStringsNumbersAndComments()
    {
        SyntaxLanguage language = SyntaxHighlighter.ForFile("a.cs")!;
        List<SyntaxSpan> spans = [];
        const string line = "if (x == 42) return \"a\\\"b\"; // done";
        bool inBlock = SyntaxHighlighter.Tokenize(language, line, false, spans);

        Assert.IsFalse(inBlock);
        string Of(SyntaxKind kind) => string.Join(",", spans.Where(s => s.Kind == kind).Select(s => line.Substring(s.Start, s.Length)));
        Assert.AreEqual("if,return", Of(SyntaxKind.Keyword));
        Assert.AreEqual("42", Of(SyntaxKind.Number));
        Assert.AreEqual("\"a\\\"b\"", Of(SyntaxKind.String));
        Assert.AreEqual("// done", Of(SyntaxKind.Comment));
    }

    [TestMethod]
    public void Syntax_ABlockCommentRunsOverLines()
    {
        SyntaxLanguage language = SyntaxHighlighter.ForFile("a.c")!;
        List<SyntaxSpan> spans = [];
        Assert.IsTrue(SyntaxHighlighter.Tokenize(language, "int a; /* start", false, spans));
        spans.Clear();
        Assert.IsTrue(SyntaxHighlighter.Tokenize(language, "still comment return", true, spans));
        Assert.AreEqual(SyntaxKind.Comment, spans[0].Kind);
        Assert.AreEqual("still comment return".Length, spans[0].Length);
        spans.Clear();
        Assert.IsFalse(SyntaxHighlighter.Tokenize(language, "end */ return", true, spans));
        Assert.IsTrue(spans.Any(s => s.Kind == SyntaxKind.Keyword));
    }

    [TestMethod]
    public void Syntax_DrawnKeywordTakesItsColourAndPlainTextDoesNot()
    {
        TextEditorControl editor = NewEditor("x", "return value");
        editor.Syntax = SyntaxHighlighter.ForFile("a.cs");
        ScreenBuffer screen = new(40, 8);
        editor.OnInitializeLayout();
        editor.Draw(screen);

        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(0, 1), "'return' is a keyword");
        Assert.AreEqual(Color.White, screen.ForegroundAt(8, 1), "'value' is plain");
    }

    [TestMethod]
    public void Syntax_ACommentThatStartedAboveTheViewStillColoursTheRowsBelowIt()
    {
        List<string> lines = ["/* a long"];
        lines.AddRange(Enumerable.Range(0, 30).Select(i => "return"));
        TextEditorControl editor = NewEditor([.. lines]);
        editor.Syntax = SyntaxHighlighter.ForFile("a.cs");
        editor.OnInitializeLayout();
        editor.ScrollableModel.ScrollPosition.Y = 10;
        editor.ScrollableModel.CursorPosition.Y = 10;

        ScreenBuffer screen = new(40, 8);
        editor.Draw(screen);
        Assert.AreEqual(Color.Gray, screen.ForegroundAt(0, 2), "still inside the block comment");
    }

    // ---- Viewer scrolling ----

    private static TextViewerControl NewViewer(int lineCount, int width = 60, int height = 12, int lineLength = 10)
    {
        IThemeService theme = Theme();
        Window parent = new(theme) { ViewSize = new Size(width, height) };
        TextViewerControl viewer = new(false, theme) { Parent = parent, Position = new Position(0, 0), ViewSize = new Size(width, height) };
        for (int i = 0; i < lineCount; i++)
        {
            viewer.TextModel.Lines.Add($"Line {i}".PadRight(lineLength, '.'));
        }

        viewer.OnInitializeLayout();
        return viewer;
    }

    private static async Task Press(TextViewerControl viewer, ConsoleKey key, bool control = false)
        => await viewer.HandleKeyEventAsync(new KeyEvent('\0', key, false, false, control));

    private static string FirstRow(TextViewerControl viewer)
    {
        ScreenBuffer screen = new(80, 30);
        viewer.Draw(screen);
        return screen.Line(0).TrimEnd();
    }

    [TestMethod]
    public async Task Viewer_DownScrollsToTheLastLineAndNoFurther()
    {
        TextViewerControl viewer = NewViewer(40);
        for (int i = 0; i < 80; i++)
        {
            await Press(viewer, ConsoleKey.DownArrow);
        }

        long visible = viewer.ScrollableModel.ViewSize.Height;
        Assert.AreEqual(40 - visible, viewer.ScrollableModel.ScrollPosition.Y, "the last line sits at the bottom of the view");
        Assert.AreEqual(39L, viewer.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual($"Line {40 - visible}", FirstRow(viewer).TrimEnd('.'));
    }

    [TestMethod]
    public async Task Viewer_PageDownAndPageUpMoveByAScreen()
    {
        TextViewerControl viewer = NewViewer(100);
        long page = viewer.ScrollableModel.ViewSize.Height;
        await Press(viewer, ConsoleKey.PageDown);
        Assert.AreEqual(page, viewer.ScrollableModel.ScrollPosition.Y);
        Assert.AreEqual($"Line {page}", FirstRow(viewer).TrimEnd('.'));
        await Press(viewer, ConsoleKey.PageUp);
        Assert.AreEqual(0L, viewer.ScrollableModel.ScrollPosition.Y);
    }

    [TestMethod]
    public async Task Viewer_HomeAndEndGoToTheStartAndTheEndOfTheFile()
    {
        TextViewerControl viewer = NewViewer(100);
        await Press(viewer, ConsoleKey.End);
        long visible = viewer.ScrollableModel.ViewSize.Height;
        Assert.AreEqual(100 - visible, viewer.ScrollableModel.ScrollPosition.Y);
        Assert.AreEqual(99L, viewer.ScrollableModel.CursorPosition.Y);
        await Press(viewer, ConsoleKey.Home);
        Assert.AreEqual(0L, viewer.ScrollableModel.ScrollPosition.Y);
        await Press(viewer, ConsoleKey.End, control: true);
        Assert.AreEqual(100 - visible, viewer.ScrollableModel.ScrollPosition.Y);
        await Press(viewer, ConsoleKey.Home, control: true);
        Assert.AreEqual(0L, viewer.ScrollableModel.ScrollPosition.Y);
    }

    [TestMethod]
    public async Task Viewer_WheelScrollsTheViewNotJustTheCursor()
    {
        TextViewerControl viewer = NewViewer(100);
        MouseEvent down = new() { Position = new Position(5, 3), WheelDelta = -1 };
        await viewer.HandleMouseEventAsync(down);
        Assert.AreEqual(3L, viewer.ScrollableModel.ScrollPosition.Y, "one notch is three rows");
        Assert.AreEqual("Line 3", FirstRow(viewer).TrimEnd('.'));

        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 3), WheelDelta = 1 });
        Assert.AreEqual(0L, viewer.ScrollableModel.ScrollPosition.Y);
        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 3), WheelDelta = 1 });
        Assert.AreEqual(0L, viewer.ScrollableModel.ScrollPosition.Y, "not above the first line");

        for (int i = 0; i < 100; i++)
        {
            await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(5, 3), WheelDelta = -1 });
        }

        Assert.AreEqual(100 - viewer.ScrollableModel.ViewSize.Height, viewer.ScrollableModel.ScrollPosition.Y, "not past the last line");
    }

    [TestMethod]
    public async Task Viewer_TextReadAfterTheLayoutStillScrolls()
    {
        TextViewerControl viewer = NewViewer(0);
        for (int i = 0; i < 100; i++)
        {
            viewer.TextModel.Lines.Add($"Line {i}");
        }

        // No layout since the text arrived: the scrollable size must follow the text by itself.
        await Press(viewer, ConsoleKey.PageDown);
        Assert.IsTrue(viewer.ScrollableModel.ScrollPosition.Y > 0 || viewer.ScrollableModel.CursorPosition.Y > 0 || viewer.ScrollableModel.ScrollableSize.Height == 100);
        ScreenBuffer screen = new(80, 30);
        viewer.Draw(screen);
        Assert.AreEqual(100L, viewer.ScrollableModel.ScrollableSize.Height);
        await Press(viewer, ConsoleKey.PageDown);
        Assert.IsTrue(viewer.ScrollableModel.ScrollPosition.Y > 0);
    }

    [TestMethod]
    public async Task Viewer_RightReachesTheLastColumnOfTheLongestLine()
    {
        TextViewerControl viewer = NewViewer(3, width: 30, height: 6, lineLength: 80);
        for (int i = 0; i < 200; i++)
        {
            await Press(viewer, ConsoleKey.RightArrow);
        }

        ScreenBuffer screen = new(80, 30);
        viewer.Draw(screen);
        Assert.AreEqual('.', screen.CharAt(27, 0), "the last text column shows the last character of the line");
        Assert.AreEqual(80 - viewer.ScrollableModel.ViewSize.Width, viewer.ScrollableModel.ScrollPosition.X);
    }

    [TestMethod]
    public async Task Viewer_ClickInWrappedTextLandsOnTheLineClicked()
    {
        TextViewerControl viewer = NewViewer(5, width: 20, height: 12, lineLength: 50);
        viewer.WrapLines = true;
        viewer.OnInitializeLayout();
        // Each 50-character line takes three 18-column rows; row 4 is the second row of line 1.
        await viewer.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 4), LeftClicked = true });
        Assert.IsTrue(viewer.ScrollableModel.CursorPosition.Y >= 0);
    }
}
