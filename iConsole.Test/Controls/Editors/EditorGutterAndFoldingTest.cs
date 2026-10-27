using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Editors;

[TestClass]
public class EditorGutterAndFoldingTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static readonly string[] Code =
    [
        "class A",          // 0  folds 1..4
        "    void M()",     // 1  folds 2..3
        "        one();",   // 2
        "        two();",   // 3
        "    int x;",       // 4
        "",                 // 5
        "class B",          // 6  folds 7
        "    int y;",       // 7
        "end",              // 8
    ];

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

    private static void Has(string actual, string expected, string? note = null) =>
        Assert.IsTrue(actual.Contains(expected, StringComparison.Ordinal), $"[{actual}] lacks [{expected}] {note}");

    private static ScreenBuffer Draw(TextEditorControl editor)
    {
        var screen = new ScreenBuffer((int)editor.ViewSize.Width, (int)editor.ViewSize.Height);
        editor.Draw(screen);
        editor.Draw(screen);
        return screen;
    }

    private static Task<bool> Press(TextEditorControl editor, ConsoleKey key, bool alt = false)
        => editor.HandleKeyEventAsync(new KeyEvent('\0', key, false, alt, false));

    // ---- line numbers ----

    [TestMethod]
    public void LineNumbers_AreDrawnInAGutterAndShiftTheText()
    {
        TextEditorControl editor = NewEditor(30, 6, "alpha", "beta", "gamma");
        editor.ShowLineNumbers = true;

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual(3, editor.GutterWidth);
        Assert.AreEqual(" 1 alpha", screen.Line(0).TrimEnd());
        Assert.AreEqual(" 2 beta", screen.Line(1).TrimEnd());
        Assert.AreEqual(" 3 gamma", screen.Line(2).TrimEnd());
        Assert.AreEqual(new ColorScheme().DisabledForeground, screen.ForegroundAt(1, 1), "the number of another line is dimmed");
    }

    [TestMethod]
    public void TheGutterGrowsWithTheNumberOfLines()
    {
        TextEditorControl editor = NewEditor(30, 4, [.. Enumerable.Range(1, 1200).Select(i => $"line {i}")]);

        editor.ShowLineNumbers = true;

        Assert.AreEqual(5, editor.GutterWidth);
        Has(Draw(editor).Line(0), "   1 line 1");
    }

    [TestMethod]
    public void WithoutNumbersOrFolding_TheLayoutIsTheOldOne()
    {
        TextEditorControl editor = NewEditor(30, 6, "alpha");

        Assert.AreEqual(0, editor.GutterWidth);
        Assert.IsFalse(editor.RowMode);
        Assert.AreEqual("alpha", Draw(editor).Line(0).TrimEnd());
    }

    [TestMethod]
    public void WrappedContinuationRowsHaveABlankGutter()
    {
        TextEditorControl editor = NewEditor(14, 6, "abcdefghijklmnopqrstuvwxyz");
        editor.WrapLines = true;
        editor.ShowLineNumbers = true;

        ScreenBuffer screen = Draw(editor);

        Assert.IsTrue(screen.Line(0).StartsWith(" 1 abcdefghi"));
        Assert.IsTrue(screen.Line(1).StartsWith("   jklmnopqr"), screen.Line(1));
        Assert.IsFalse(screen.Line(1).Substring(0, 3).Any(char.IsDigit));
    }

    [TestMethod]
    public void TheCaretSitsRightOfTheGutter()
    {
        TextEditorControl editor = NewEditor(30, 6, "alpha", "beta");
        editor.ShowLineNumbers = true;
        editor.ShowLine(1, 2);

        Position caret = editor.CursorPosition;

        Assert.AreEqual(3 + 2, (int)caret.X);
        Assert.AreEqual(1, (int)caret.Y);
    }

    [TestMethod]
    public async Task AClickOnTheTextLandsOnTheRightColumn()
    {
        TextEditorControl editor = NewEditor(30, 6, "alpha", "beta");
        editor.ShowLineNumbers = true;
        Draw(editor);

        await editor.HandleMouseEventAsync(new MouseEvent { Position = new Position(3 + 2, 1), LeftClicked = true });

        Assert.AreEqual(1, editor.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual(2, editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task TypingStillEditsTheLineUnderTheGutter()
    {
        TextEditorControl editor = NewEditor(30, 6, "ac");
        editor.ShowLineNumbers = true;
        editor.ShowLine(0, 1);

        await editor.HandleKeyEventAsync(new KeyEvent('b', ConsoleKey.B, false, false, false));

        Assert.AreEqual("abc", editor.TextModel.Lines[0]);
        Has(Draw(editor).Line(0), "1 abc");
    }

    // ---- soft wrap (already there: the gutter must not break it) ----

    [TestMethod]
    public void SoftWrapAndNumbers_FlowTheTextIntoTheWidthLeftByTheGutter()
    {
        TextEditorControl editor = NewEditor(14, 6, "abcdefghijklmnopqrstuvwxyz");
        editor.WrapLines = true;
        editor.ShowLineNumbers = true;

        Assert.AreEqual(3, editor.ScreenRowLines.Count);
        Assert.AreEqual(3 + 9, Draw(editor).Line(0).TrimEnd().Length);
    }

    // ---- folding ----

    [TestMethod]
    public void FoldRegion_RunsToTheLastDeeperLine_SkippingBlankLines()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);

        Assert.AreEqual((1, 4), editor.FoldRegion(0));
        Assert.AreEqual((2, 3), editor.FoldRegion(1));
        Assert.IsNull(editor.FoldRegion(2), "a line with nothing deeper after it");
        Assert.IsNull(editor.FoldRegion(5), "a blank line");
        Assert.AreEqual((7, 7), editor.FoldRegion(6));
        Assert.IsNull(editor.FoldRegion(99));
        Assert.IsTrue(editor.CanFold(0));
        Assert.IsFalse(editor.CanFold(8));
    }

    [TestMethod]
    public void Fold_HidesTheRegionAndDrawsANote_AndUnfoldBringsItBack()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        int before = editor.ScreenRowLines.Count;

        Assert.IsTrue(editor.Fold(0));

        Assert.AreEqual(before - 4, editor.ScreenRowLines.Count);
        Assert.IsTrue(editor.IsFolded(0));
        Assert.IsTrue(editor.IsHidden(3));
        ScreenBuffer screen = Draw(editor);
        Has(screen.Line(0), "▸ class A");
        Has(screen.Line(0), "⋯ 4 lines");
        Has(screen.Line(2), "class B", "the blank line 6 follows the fold, then class B");
        Assert.IsFalse(screen.Contains("one();"));

        Assert.IsTrue(editor.Unfold(0));
        ScreenBuffer open = Draw(editor);
        Has(open.Line(0), "▾ class A");
        Assert.IsTrue(open.Contains("one();"));
    }

    [TestMethod]
    public void Markers_ShowWhereAFoldIsPossible()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual('▾', screen.CharAt(0, 0));
        Assert.AreEqual(' ', screen.CharAt(0, 2), "nothing to fold under one();");
        Assert.AreEqual(2, editor.GutterWidth);
    }

    [TestMethod]
    public void FoldingIsOffUnlessEnabled()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);

        Assert.IsFalse(editor.Fold(0));
        Assert.AreEqual(0, editor.FoldedCount);
    }

    [TestMethod]
    public void ToggleFold_FromInsideARegionFoldsTheOneThatHoldsIt()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;

        Assert.AreEqual(1, editor.FoldOwner(3));
        Assert.IsTrue(editor.ToggleFold(3));
        Assert.IsTrue(editor.IsFolded(1));
        Assert.IsFalse(editor.IsFolded(0));

        Assert.IsTrue(editor.ToggleFold(3), "a hidden line toggles the fold that hides it");
        Assert.IsFalse(editor.IsFolded(1));
    }

    [TestMethod]
    public void FoldAll_ClosesEveryRegion_AndUnfoldAllOpensThem()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;

        editor.FoldAll();

        Assert.AreEqual(3, editor.FoldedCount);
        CollectionAssert.AreEqual(new[] { 0, 5, 6, 8 }, editor.ScreenRowLines.ToArray());

        editor.UnfoldAll();
        Assert.AreEqual(0, editor.FoldedCount);
        Assert.AreEqual(Code.Length, editor.ScreenRowLines.Count);
    }

    [TestMethod]
    public void FoldingTheRegionHoldingTheCursor_MovesTheCursorToTheFoldLine()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        editor.ShowLine(2, 5);

        editor.Fold(1);

        Assert.AreEqual(1, editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task AltLeftFolds_AltRightUnfolds_AtTheCursorLine()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        editor.ShowLine(0, 0);

        Assert.IsTrue(await Press(editor, ConsoleKey.LeftArrow, alt: true));
        Assert.IsTrue(editor.IsFolded(0));

        Assert.IsTrue(await Press(editor, ConsoleKey.RightArrow, alt: true));
        Assert.IsFalse(editor.IsFolded(0));

        editor.ShowLine(8, 0);
        Assert.IsFalse(await Press(editor, ConsoleKey.LeftArrow, alt: true), "nothing to fold on the last line");
    }

    [TestMethod]
    public async Task UpAndDown_SkipTheLinesAFoldHides()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        editor.Fold(0);
        editor.ShowLine(0, 0);

        await Press(editor, ConsoleKey.DownArrow);
        Assert.AreEqual(5, editor.ScrollableModel.CursorPosition.Y, "the blank line right after the fold");

        await Press(editor, ConsoleKey.UpArrow);
        Assert.AreEqual(0, editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task AClickOnAMarkerTogglesTheFold()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        Draw(editor);

        await editor.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });
        Assert.IsTrue(editor.IsFolded(0));

        await editor.HandleMouseEventAsync(new MouseEvent { Position = new Position(0, 0), LeftClicked = true });
        Assert.IsFalse(editor.IsFolded(0));
    }

    [TestMethod]
    public void ShowLine_OpensTheFoldThatHidesTheLine()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        editor.Fold(0);

        editor.ShowLine(3, 0);

        Assert.IsFalse(editor.IsFolded(0));
        Assert.IsFalse(editor.IsHidden(3));
        Assert.AreEqual(3, editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public void AddingOrRemovingLines_ForgetsTheFolds()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        editor.Fold(0);

        editor.TextModel.Lines.Add("more");
        editor.TextModel.TriggerPropertyChanged();

        Assert.AreEqual(0, editor.FoldedCount);
        Assert.AreEqual(Code.Length + 1, editor.ScreenRowLines.Count);
    }

    [TestMethod]
    public void FoldingWithNumbers_ShowsTheRealLineNumbers()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        editor.EnableFolding = true;
        editor.ShowLineNumbers = true;
        editor.Fold(0);

        ScreenBuffer screen = Draw(editor);

        Has(screen.Line(0), "▸ 1 class A");
        Has(screen.Line(2), "▾ 7 class B", "the hidden lines still count");
    }

    [TestMethod]
    public void KeyHints_MentionFoldingOnlyWhenItIsOn()
    {
        TextEditorControl editor = NewEditor(40, 12, Code);
        Assert.IsFalse(editor.GetKeyHints().Any(h => h.Key.Contains("Alt+Left")));

        editor.EnableFolding = true;
        Assert.IsTrue(editor.GetKeyHints().Any(h => h.Key.Contains("Alt+Left")));
    }

    [TestMethod]
    public void EmptyTextAndTinyViews_DoNotThrow()
    {
        TextEditorControl empty = NewEditor(30, 6);
        empty.ShowLineNumbers = true;
        empty.EnableFolding = true;
        empty.FoldAll();
        Draw(empty);

        TextEditorControl tiny = NewEditor(6, 3, Code);
        tiny.ShowLineNumbers = true;
        tiny.EnableFolding = true;
        tiny.FoldAll();
        Draw(tiny);
        tiny.UnfoldAll();
        Draw(tiny);
    }
}
