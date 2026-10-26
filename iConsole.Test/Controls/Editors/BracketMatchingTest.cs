using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Editors;

[TestClass]
public class BracketMatchingTest
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

        editor.OnInitializeLayout();
        return editor;
    }

    private static void PlaceCursor(TextEditorControl editor, int line, int column)
    {
        editor.ScrollableModel.CursorPosition.Y = line;
        editor.ScrollableModel.CursorPosition.X = column;
    }

    // ---- BracketMatcher ----

    [TestMethod]
    public void AnOpeningBracketFindsItsClosingOne()
    {
        BracketMatch? match = BracketMatcher.Find(["(a[b]c)"], 0, 0);

        Assert.AreEqual(new BracketLocation(0, 0), match!.Bracket);
        Assert.AreEqual(new BracketLocation(0, 6), match.Match);
    }

    [TestMethod]
    public void AClosingBracketBeforeTheCursorFindsItsOpeningOne()
    {
        // The cursor is after the last character; the bracket just before it is the one that counts.
        BracketMatch? match = BracketMatcher.Find(["(a[b]c)"], 0, 7);

        Assert.AreEqual(new BracketLocation(0, 6), match!.Bracket);
        Assert.AreEqual(new BracketLocation(0, 0), match.Match);
    }

    [TestMethod]
    public void TheBracketUnderTheCursorWinsOverTheOneBefore()
    {
        BracketMatch? atClosing = BracketMatcher.Find(["()"], 0, 1);
        BracketMatch? atOpening = BracketMatcher.Find(["()"], 0, 0);

        Assert.AreEqual(new BracketLocation(0, 1), atClosing!.Bracket);
        Assert.AreEqual(new BracketLocation(0, 0), atClosing.Match);
        Assert.AreEqual(new BracketLocation(0, 0), atOpening!.Bracket);
        Assert.AreEqual(new BracketLocation(0, 1), atOpening.Match);
    }

    [TestMethod]
    public void NestedBracketsPairInnermostFirst()
    {
        Assert.AreEqual(new BracketLocation(0, 4), BracketMatcher.Find(["((a))"], 0, 0)!.Match);
        Assert.AreEqual(new BracketLocation(0, 3), BracketMatcher.Find(["((a))"], 0, 1)!.Match);
    }

    [TestMethod]
    public void OnlyBracketsOfTheSameKindAreCounted()
    {
        // The [ in between does not open a level for the ( looking for its partner.
        Assert.AreEqual(new BracketLocation(0, 4), BracketMatcher.Find(["( [ ) ]"], 0, 0)!.Match);
    }

    [TestMethod]
    public void AMatchCanBeOnAnotherLine()
    {
        string[] lines = ["if (x) {", "  y();", "}"];

        Assert.AreEqual(new BracketLocation(2, 0), BracketMatcher.Find(lines, 0, 7)!.Match);
        Assert.AreEqual(new BracketLocation(0, 7), BracketMatcher.Find(lines, 2, 0)!.Match);
    }

    [TestMethod]
    public void AnUnclosedBracketHasNoMatch()
    {
        BracketMatch? match = BracketMatcher.Find(["(a"], 0, 0);

        Assert.IsNotNull(match);
        Assert.IsNull(match.Match);
    }

    [TestMethod]
    public void NoBracketNearTheCursorIsNoResult()
    {
        Assert.IsNull(BracketMatcher.Find(["abc"], 0, 1));
        Assert.IsNull(BracketMatcher.Find(["abc"], 5, 0));
        Assert.IsNull(BracketMatcher.Find([], 0, 0));
        Assert.IsNull(BracketMatcher.Find(["(x)"], 0, -1));
    }

    [TestMethod]
    public void ASearchThatWouldCrossTooManyLinesGivesUp()
    {
        List<string> lines = ["("];
        lines.AddRange(Enumerable.Repeat("x", BracketMatcher.MaxLinesScanned + 500));
        lines.Add(")");

        BracketMatch? match = BracketMatcher.Find(lines, 0, 0);

        Assert.IsNotNull(match);
        Assert.IsNull(match.Match, "a far partner is not worth a slow frame");
    }

    // ---- The editor ----

    [TestMethod]
    public void TheEditorFindsTheBracketAtItsCursor()
    {
        TextEditorControl editor = NewEditor("f(a, [b])");
        PlaceCursor(editor, 0, 1);

        BracketMatch? match = editor.FindMatchingBracket();

        Assert.AreEqual(new BracketLocation(0, 8), match!.Match);
    }

    [TestMethod]
    public void GoToMatchingBracketMovesTheCursorThere()
    {
        TextEditorControl editor = NewEditor("f(a, [b])");
        PlaceCursor(editor, 0, 1);

        Assert.IsTrue(editor.GoToMatchingBracket());
        Assert.AreEqual(8, (int)editor.ScrollableModel.CursorPosition.X);

        Assert.IsTrue(editor.GoToMatchingBracket(), "and back");
        Assert.AreEqual(1, (int)editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public void GoToMatchingBracketDoesNothingWithoutAPartner()
    {
        TextEditorControl editor = NewEditor("f(a", "plain");
        PlaceCursor(editor, 0, 1);
        Assert.IsFalse(editor.GoToMatchingBracket(), "unclosed");

        PlaceCursor(editor, 1, 2);
        Assert.IsFalse(editor.GoToMatchingBracket(), "not by a bracket");
        Assert.AreEqual(2, (int)editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task CtrlF9GoesToTheMatchingBracket()
    {
        TextEditorControl editor = NewEditor("{ a }");
        PlaceCursor(editor, 0, 0);

        bool handled = await editor.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F9, false, false, true));

        Assert.IsTrue(handled);
        Assert.AreEqual(4, (int)editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task PlainF9StillGoesToTheNextBookmark()
    {
        TextEditorControl editor = NewEditor("{ a }", "b", "c");
        PlaceCursor(editor, 2, 0);
        editor.ToggleBookmark();
        PlaceCursor(editor, 0, 0);

        await editor.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F9, false, false, false));

        Assert.AreEqual(2, (int)editor.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public void BothBracketsOfAPairAreMarkedWhenDrawn()
    {
        TextEditorControl editor = NewEditor("x (a) y");
        PlaceCursor(editor, 0, 2);
        ScreenBuffer screen = new(40, 8);

        editor.Draw(screen);

        Assert.AreEqual(Color.Cyan, screen.BackgroundAt(2, 0));
        Assert.AreEqual(Color.Cyan, screen.BackgroundAt(4, 0));
        Assert.IsTrue(screen.Cell(2, 0).IsBold);
        Assert.AreNotEqual(Color.Cyan, screen.BackgroundAt(3, 0));
        Assert.AreNotEqual(Color.Cyan, screen.BackgroundAt(0, 0));
    }

    [TestMethod]
    public void ABracketWithoutAPartnerIsMarkedInRed()
    {
        TextEditorControl editor = NewEditor("x (a y");
        PlaceCursor(editor, 0, 2);
        ScreenBuffer screen = new(40, 8);

        editor.Draw(screen);

        Assert.AreEqual(Color.Red, screen.BackgroundAt(2, 0));
    }

    [TestMethod]
    public void NothingIsMarkedWhenTheFeatureIsOff()
    {
        TextEditorControl editor = NewEditor("x (a) y");
        editor.BracketMatchingEnabled = false;
        PlaceCursor(editor, 0, 2);
        ScreenBuffer screen = new(40, 8);

        editor.Draw(screen);

        Assert.AreNotEqual(Color.Cyan, screen.BackgroundAt(2, 0));
        Assert.IsFalse(editor.GetKeyHints().Any(hint => hint.Key == "Ctrl+F9"));
    }

    [TestMethod]
    public void AMarkFollowsTheTextWhenItIsScrolledSideways()
    {
        TextEditorControl editor = NewEditor("0123456789 (a) z");
        PlaceCursor(editor, 0, 11);
        editor.ScrollableModel.ScrollPosition.X = 5;
        ScreenBuffer screen = new(40, 8);

        editor.Draw(screen);

        // Column 11 is the sixth cell when the view starts at column 5.
        Assert.AreEqual(Color.Cyan, screen.BackgroundAt(6, 0));
        Assert.AreEqual(Color.Cyan, screen.BackgroundAt(8, 0));
    }

    [TestMethod]
    public void AMatchOnAnotherLineIsMarkedToo()
    {
        TextEditorControl editor = NewEditor("{", "  a", "}");
        PlaceCursor(editor, 0, 0);
        ScreenBuffer screen = new(40, 8);

        editor.Draw(screen);

        Assert.AreEqual(Color.Cyan, screen.BackgroundAt(0, 0));
        Assert.AreEqual(Color.Cyan, screen.BackgroundAt(0, 2));
    }

    [TestMethod]
    public void AWrappedLineMarksTheBracketOnTheRowThatHoldsIt()
    {
        TextEditorControl editor = NewEditor("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa(b)");
        editor.ViewSize = new Size(22, 8);
        editor.OnInitializeLayout();
        editor.WrapLines = true;
        PlaceCursor(editor, 0, 46);
        ScreenBuffer screen = new(40, 8);

        editor.Draw(screen);

        bool anyMarked = Enumerable.Range(0, 8).Any(y => Enumerable.Range(0, 22).Any(x => screen.BackgroundAt(x, y) == Color.Cyan));
        Assert.IsTrue(anyMarked, "the pair is marked in the wrapped view");
    }
}
