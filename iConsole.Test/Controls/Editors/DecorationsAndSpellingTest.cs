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

[TestClass]
public class DecorationsAndSpellingTest
{
    private static readonly string[] Words =
    [
        "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "hello", "world", "spelling", "is", "fun", "color", "colour",
        "receive", "believe", "separate", "this", "a", "test", "of", "and", "john", "cat", "cart", "care", "cats",
    ];

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

    private static ScreenBuffer Draw(TextEditorControl editor)
    {
        var screen = new ScreenBuffer((int)editor.ViewSize.Width, (int)editor.ViewSize.Height);
        editor.Draw(screen);
        editor.Draw(screen);
        return screen;
    }

    // ---- DecorationSet ----

    [TestMethod]
    public void TheSetKeepsMarksByLine_AndLowestPriorityComesFirst()
    {
        var set = new DecorationSet();
        set.Add(new TextDecoration(2, 0, 3, Priority: 5));
        set.Add(new TextDecoration(2, 1, 2, Priority: 1));
        set.Add(new TextDecoration(7, 0, 1));

        Assert.AreEqual(3, set.Count);
        CollectionAssert.AreEqual(new[] { 1, 5 }, set.ForLine(2).Select(d => d.Priority).ToArray());
        Assert.AreEqual(0, set.ForLine(3).Count);
    }

    [TestMethod]
    public void ReplaceSource_SwapsOneSourcesMarks_AndLeavesTheOthers()
    {
        var set = new DecorationSet();
        set.Add(new TextDecoration(0, 0, 1, "diff"));
        set.ReplaceSource("lsp", [new TextDecoration(1, 0, 1), new TextDecoration(2, 0, 1)]);
        long before = set.Version;

        set.ReplaceSource("lsp", [new TextDecoration(5, 0, 1)]);

        Assert.AreEqual(2, set.Count);
        Assert.IsTrue(set.All.Any(d => d.Line == 0 && d.Source == "diff"));
        Assert.IsTrue(set.All.Any(d => d.Line == 5 && d.Source == "lsp"), "the replaced marks take the source's name");
        Assert.IsTrue(set.Version > before);
        Assert.IsTrue(set.Remove("lsp"));
        Assert.IsFalse(set.Remove("lsp"));
        Assert.AreEqual(1, set.Count);
    }

    [TestMethod]
    public void MarksThatMeanNothing_AreNotKept()
    {
        var set = new DecorationSet();

        set.Add(new TextDecoration(-1, 0, 3));
        set.Add(new TextDecoration(0, -2, 3));
        set.Add(new TextDecoration(0, 0, 0));

        Assert.AreEqual(0, set.Count);
    }

    [TestMethod]
    public void MessageAt_FindsTheHighestPriorityMarkWithAMessageAtThatColumn()
    {
        var set = new DecorationSet();
        set.Add(new TextDecoration(0, 0, 10, Message: "wide", Priority: 1));
        set.Add(new TextDecoration(0, 3, 2, Message: "narrow", Priority: 2));
        set.Add(new TextDecoration(0, 3, 2, Priority: 9));

        Assert.AreEqual("narrow", set.MessageAt(0, 4)!.Message);
        Assert.AreEqual("wide", set.MessageAt(0, 8)!.Message);
        Assert.IsNull(set.MessageAt(0, 10));
        Assert.IsNull(set.MessageAt(4, 0));
    }

    [TestMethod]
    public async Task ManyThreadsMayAddMarks()
    {
        var set = new DecorationSet();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 200; i++)
            {
                set.Add(new TextDecoration(i, t, 1));
            }
        })));

        Assert.AreEqual(1600, set.Count);
    }

    // ---- decorations drawn ----

    [TestMethod]
    public void ARangeMark_ColoursOnlyItsCharacters()
    {
        TextEditorControl editor = NewEditor(30, 5, "first", "hello world");
        editor.Decorations.Add(new TextDecoration(1, 6, 5, Foreground: Color.Red, Background: Color.DarkBlue, Bold: true));

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual(Color.Red, screen.ForegroundAt(6, 1));
        Assert.AreEqual(Color.DarkBlue, screen.BackgroundAt(10, 1));
        Assert.AreNotEqual(Color.DarkBlue, screen.BackgroundAt(5, 1));
        Assert.AreNotEqual(Color.DarkBlue, screen.BackgroundAt(11, 1));
        Assert.IsTrue(screen.CellOf("world").IsBold);
    }

    [TestMethod]
    public void AWholeLineMark_FillsTheRowIncludingThePadding()
    {
        TextEditorControl editor = NewEditor(30, 5, "first", "added line");
        editor.Decorations.Add(TextDecoration.WholeLine(1, "diff", background: Color.DarkGreen));

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual(Color.DarkGreen, screen.BackgroundAt(0, 1));
        Assert.AreEqual(Color.DarkGreen, screen.BackgroundAt(25, 1), "past the text, to the edge of the row");
        Assert.AreNotEqual(Color.DarkGreen, screen.BackgroundAt(0, 2));
    }

    [TestMethod]
    public void AnUnderlineMark_SetsTheStyleAndColour()
    {
        TextEditorControl editor = NewEditor(30, 5, "first", "wrong word");
        editor.Decorations.Add(new TextDecoration(1, 0, 5, Underline: UnderlineStyle.Curly, UnderlineColor: Color.Red));

        ScreenBuffer screen = Draw(editor);

        Conxel cell = screen.Cell(2, 1);
        Assert.IsTrue(cell.IsUnderline);
        Assert.AreEqual(UnderlineStyle.Curly, cell.UnderlineStyle);
        Assert.IsTrue(cell.HasUnderlineColor);
        Assert.AreEqual(Color.Red, cell.UnderlineColor);
        Assert.IsFalse(screen.Cell(7, 1).IsUnderline);
    }

    [TestMethod]
    public void OnTheCursorLine_ColoursAreLeftAlone_ButTheUnderlineStillShows()
    {
        TextEditorControl editor = NewEditor(30, 5, "cursor line here", "second");
        Color rowBackground = Draw(editor).BackgroundAt(0, 0);
        editor.Decorations.Add(new TextDecoration(0, 0, 6, Background: Color.DarkRed, Underline: UnderlineStyle.Single));

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual(rowBackground, screen.BackgroundAt(2, 0));
        Assert.IsTrue(screen.Cell(2, 0).IsUnderline);
    }

    [TestMethod]
    public void HigherPriorityWins_WhereMarksOverlap()
    {
        TextEditorControl editor = NewEditor(30, 5, "x", "abcdef");
        editor.Decorations.Add(new TextDecoration(1, 0, 6, Foreground: Color.Red, Priority: 1));
        editor.Decorations.Add(new TextDecoration(1, 2, 2, Foreground: Color.Green, Priority: 2));

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual(Color.Red, screen.ForegroundAt(0, 1));
        Assert.AreEqual(Color.Green, screen.ForegroundAt(3, 1));
        Assert.AreEqual(Color.Red, screen.ForegroundAt(5, 1));
    }

    [TestMethod]
    public void MarksFollowTheTextWhenScrolledSidewaysAndWhenWrapped()
    {
        TextEditorControl wrapped = NewEditor(14, 6, "x", "0123456789abcdefghij");
        wrapped.WrapLines = true;
        wrapped.Decorations.Add(new TextDecoration(1, 10, 3, Foreground: Color.Red));

        ScreenBuffer screen = Draw(wrapped);

        // Twelve columns of text per row: "abc" starts at column 10, so it is the third cell of the second row of line 1... check any red cell.
        Assert.IsTrue(Enumerable.Range(0, 6).Any(row => Enumerable.Range(0, 14).Any(x => screen.ForegroundAt(x, row) == Color.Red)));

        TextEditorControl scrolled = NewEditor(10, 4, "0123456789abcdefghij", "0123456789abcdefghij");
        scrolled.Decorations.Add(new TextDecoration(1, 18, 2, Foreground: Color.Red));
        scrolled.ShowLine(0, 19);
        ScreenBuffer scrolledScreen = Draw(scrolled);
        Assert.IsTrue(Enumerable.Range(0, 10).Any(x => scrolledScreen.ForegroundAt(x, 1) == Color.Red), "the mark is in view after scrolling right");
    }

    [TestMethod]
    public void AMarkAddedFromAnotherThread_IsNoticedByTheIdlePass()
    {
        TextEditorControl editor = NewEditor(30, 5, "hello");
        Draw(editor);
        editor.HasStateChanged = false;
        editor.OnIdle();
        Assert.IsFalse(editor.HasStateChanged);

        Task.Run(() => editor.Decorations.Add(new TextDecoration(0, 0, 2, Foreground: Color.Red))).Wait();
        editor.OnIdle();

        Assert.IsTrue(editor.HasStateChanged);
    }

    // ---- SpellChecker ----

    private static SpellChecker Checker() => new(Words);

    [TestMethod]
    public void KnownWords_AreCorrect_WhateverTheirCase_AndPossessivesAndHyphenationAreAllowed()
    {
        SpellChecker checker = Checker();

        Assert.IsTrue(checker.IsCorrect("quick"));
        Assert.IsTrue(checker.IsCorrect("Quick"));
        Assert.IsTrue(checker.IsCorrect("John's"));
        Assert.IsTrue(checker.IsCorrect("quick-brown"));
        Assert.IsFalse(checker.IsCorrect("quikc"));
        Assert.IsFalse(checker.IsCorrect("quick-brwon"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("a")]
    [DataRow("HTTP")]
    [DataRow("v2")]
    [DataRow("snake_case")]
    [DataRow("camelCase")]
    [DataRow("iPhone")]
    public void ThingsThatAreNotWords_AreNotJudged(string word)
    {
        Assert.IsTrue(Checker().IsCorrect(word));
    }

    [TestMethod]
    public void Suggestions_AreCloseWordsFromTheList_BestFirst_InTheCaseOfTheOriginal()
    {
        SpellChecker checker = Checker();

        IReadOnlyList<string> teh = checker.Suggest("teh");
        IReadOnlyList<string> recieve = checker.Suggest("recieve");
        IReadOnlyList<string> capital = checker.Suggest("Quikc");

        CollectionAssert.Contains(teh.ToList(), "the");
        Assert.AreEqual("receive", recieve[0]);
        Assert.AreEqual("Quick", capital[0]);
        Assert.AreEqual(0, checker.Suggest("quick").Count, "a correct word needs none");
        Assert.AreEqual(0, checker.Suggest("zzzzzzzz").Count);
        Assert.IsTrue(checker.Suggest("cat1x", 2).Count <= 2);
    }

    [TestMethod]
    public void EditDistance_CountsASwapAsOne()
    {
        Assert.AreEqual(0, SpellChecker.Distance("fox", "fox"));
        Assert.AreEqual(1, SpellChecker.Distance("teh", "the"));
        Assert.AreEqual(1, SpellChecker.Distance("cat", "cart"));
        Assert.AreEqual(3, SpellChecker.Distance("abc", "xyz"));
    }

    [TestMethod]
    public void AddAndIgnore_MakeAWordCorrect()
    {
        var checker = Checker();

        Assert.IsFalse(checker.IsCorrect("kubernetes"));
        Assert.IsTrue(checker.Add("Kubernetes"));
        Assert.IsFalse(checker.Add("kubernetes"), "already there");
        Assert.IsTrue(checker.IsCorrect("kubernetes"));
        CollectionAssert.AreEqual(new[] { "kubernetes" }, checker.UserWords.ToArray());

        Assert.IsFalse(checker.IsCorrect("flibbertigibbet"));
        checker.Ignore("flibbertigibbet");
        Assert.IsTrue(checker.IsCorrect("flibbertigibbet"));
        Assert.AreEqual(Words.Length, checker.Count, "the list itself is not changed");
    }

    [TestMethod]
    public void Words_FindsLettersWithInnerApostrophesAndHyphens()
    {
        string text = "It's a well-known test, ok? don't--stop 'quoted'";

        string[] words = [.. SpellChecker.Words(text).Select(w => text.Substring(w.Start, w.Length))];

        CollectionAssert.AreEqual(new[] { "It's", "a", "well-known", "test", "ok", "don't", "stop", "quoted" }, words);
    }

    [TestMethod]
    public void FromFile_ReadsAPlainListAndAHunspellDictionary()
    {
        string folder = Path.Combine(Path.GetTempPath(), "sc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string plain = Path.Combine(folder, "words.txt");
            File.WriteAllLines(plain, ["# comment", "alpha", "", "Beta"]);
            string dic = Path.Combine(folder, "en.dic");
            File.WriteAllLines(dic, ["3", "gamma/MS", "delta", "epsilon/SM"]);

            SpellChecker fromPlain = SpellChecker.FromFile(plain);
            SpellChecker fromDic = SpellChecker.FromFile(dic);

            Assert.IsTrue(fromPlain.IsCorrect("alpha") && fromPlain.IsCorrect("beta"));
            Assert.AreEqual(2, fromPlain.Count);
            Assert.IsTrue(fromDic.IsCorrect("gamma") && fromDic.IsCorrect("delta") && fromDic.IsCorrect("epsilon"));
            Assert.AreEqual(3, fromDic.Count, "the count line and the flags are dropped");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    // ---- spell checking in the editor ----

    [TestMethod]
    public void Misspellings_AreUnderlinedWithACurlyLine_AndNothingElseIs()
    {
        TextEditorControl editor = NewEditor(40, 5, "first", "the quikc fox");
        editor.SpellChecker = Checker();

        ScreenBuffer screen = Draw(editor);

        Conxel wrong = screen.Cell(5, 1);
        Assert.IsTrue(wrong.IsUnderline);
        Assert.AreEqual(UnderlineStyle.Curly, wrong.UnderlineStyle);
        Assert.AreEqual(new ColorScheme().Error, wrong.UnderlineColor);
        Assert.IsFalse(screen.Cell(1, 1).IsUnderline, "'the' is right");
        Assert.IsFalse(screen.Cell(11, 1).IsUnderline, "'fox' is right");
    }

    [TestMethod]
    public void WithoutAChecker_NothingIsUnderlined()
    {
        TextEditorControl editor = NewEditor(40, 5, "first", "the quikc fox");

        Assert.IsFalse(Draw(editor).Cell(5, 1).IsUnderline);
    }

    [TestMethod]
    public void InCode_OnlyCommentsAndStringsAreChecked()
    {
        TextEditorControl editor = NewEditor(50, 5, "x", "int qwerty = 1; // the quikc fox", "string s = \"a wrnog word\";");
        editor.Syntax = SyntaxHighlighter.ForFile("a.cs");
        editor.SpellChecker = Checker();

        ScreenBuffer screen = Draw(editor);

        Assert.IsFalse(screen.Cell(4, 1).IsUnderline, "the identifier qwerty is code");
        int comment = screen.Line(1).IndexOf("quikc", StringComparison.Ordinal);
        Assert.IsTrue(screen.Cell(comment, 1).IsUnderline);
        int inString = screen.Line(2).IndexOf("wrnog", StringComparison.Ordinal);
        Assert.IsTrue(screen.Cell(inString, 2).IsUnderline);
    }

    [TestMethod]
    public void MisspelledWordAt_SuggestionsAt_AndAddWord_WorkOnTheTextUnderTheCursor()
    {
        var checker = Checker();
        TextEditorControl editor = NewEditor(40, 5, "first", "the quikc fox");
        editor.SpellChecker = checker;

        (string Word, int Start)? found = editor.MisspelledWordAt(1, 6);
        Assert.AreEqual("quikc", found!.Value.Word);
        Assert.AreEqual(4, found.Value.Start);
        Assert.IsNotNull(editor.MisspelledWordAt(1, 9), "just past the last letter still counts");
        Assert.IsNull(editor.MisspelledWordAt(1, 1));
        Assert.IsNull(editor.MisspelledWordAt(9, 0));
        CollectionAssert.Contains(editor.SuggestionsAt(1, 6).ToList(), "quick");
        Assert.AreEqual(0, editor.SuggestionsAt(1, 1).Count);

        Assert.IsTrue(editor.AddWordToDictionary(1, 6));
        Assert.IsNull(editor.MisspelledWordAt(1, 6));
        Assert.IsFalse(Draw(editor).Cell(5, 1).IsUnderline, "no longer underlined");
        Assert.IsFalse(editor.AddWordToDictionary(1, 6));
    }

    [TestMethod]
    public void TypingANewWord_IsCheckedAtOnce()
    {
        TextEditorControl editor = NewEditor(40, 5, "x", "the dog");
        editor.SpellChecker = Checker();
        Assert.IsFalse(Draw(editor).Cell(5, 1).IsUnderline);

        editor.TextModel.Lines[1] = "the dxg";

        Assert.IsTrue(Draw(editor).Cell(5, 1).IsUnderline);
    }

    [TestMethod]
    public void TheIdlePassRepaints_WhenTheDictionaryChanged()
    {
        TextEditorControl editor = NewEditor(40, 5, "the quikc fox");
        editor.SpellChecker = Checker();
        Draw(editor);
        editor.HasStateChanged = false;
        editor.OnIdle();
        Assert.IsFalse(editor.HasStateChanged);

        editor.ResetSpellCache();
        editor.HasStateChanged = false;
        editor.OnIdle();

        Assert.IsTrue(editor.HasStateChanged);
    }
}
