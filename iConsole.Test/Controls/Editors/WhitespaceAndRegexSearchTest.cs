using System.Reflection;
using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Editors;

[TestClass]
public class WhitespaceAndRegexSearchTest
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

    private static ScreenBuffer Draw(TextEditorControl editor)
    {
        var screen = new ScreenBuffer((int)editor.ViewSize.Width, (int)editor.ViewSize.Height);
        editor.Draw(screen);
        editor.Draw(screen);
        return screen;
    }

    // ---- visible whitespace ----

    [TestMethod]
    public void Whitespace_IsDrawnAsMarks_OnlyWhenAsked_AndTheTextIsUntouched()
    {
        TextEditorControl editor = NewEditor(30, 5, "a b\tc");

        Assert.AreEqual("a b\tc".Replace('\t', ' '), Draw(editor).Line(0).TrimEnd().Replace('\t', ' '));
        editor.ShowWhitespace = true;
        string line = Draw(editor).Line(0);

        Assert.IsTrue(line.StartsWith("a·b→c"), line);
        Assert.AreEqual("a b\tc", editor.TextModel.Lines[0]);
    }

    [TestMethod]
    public void TrailingWhitespace_IsDrawnInTheErrorColour_AndInnerWhitespaceDimmed()
    {
        TextEditorControl editor = NewEditor(30, 5, "ab  cd  ", "x");
        editor.ShowWhitespace = true;
        ColorScheme scheme = new();

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual('·', screen.CharAt(2, 0));
        Assert.AreEqual('·', screen.CharAt(6, 0));
        Assert.AreEqual('·', screen.CharAt(7, 0));
        // Row 0 is the cursor line in the viewer, so look at the colours on a row that is not.
        TextEditorControl second = NewEditor(30, 5, "x", "ab  cd  ");
        second.ShowWhitespace = true;
        ScreenBuffer other = Draw(second);
        Assert.AreEqual(scheme.DisabledForeground, other.ForegroundAt(2, 1));
        Assert.AreEqual(scheme.Error, other.ForegroundAt(6, 1));
        Assert.AreEqual(scheme.Error, other.ForegroundAt(7, 1));
    }

    [TestMethod]
    public void Whitespace_WorksWithTheGutterAndWrapping_AndNothingIsMarkedPastTheEndOfALine()
    {
        TextEditorControl editor = NewEditor(24, 6, "one two three four five six");
        editor.ShowLineNumbers = true;
        editor.WrapLines = true;
        editor.ShowWhitespace = true;

        ScreenBuffer screen = Draw(editor);

        Assert.IsTrue(screen.Line(0).Contains("one·two"), screen.Line(0));
        Assert.IsFalse(screen.Line(4).Contains('·'), "the empty rows below the text have no marks");
    }

    [TestMethod]
    public void Whitespace_ScrolledSideways_StillMarksTheVisiblePart()
    {
        TextEditorControl editor = NewEditor(12, 4, "abcdefgh ijklmnop qrst");
        editor.ShowWhitespace = true;
        editor.ShowLine(0, 20);

        ScreenBuffer screen = Draw(editor);

        Assert.IsTrue(screen.Line(0).Contains('·'), screen.Line(0));
    }

    // ---- SearchEngine ----

    [TestMethod]
    public void PlainSearch_FindsEveryOccurrence_RespectingCase()
    {
        string[] lines = ["Abc abc", "ABC"];

        IReadOnlyList<SearchHit> exact = SearchEngine.FindAll(lines, "abc", useRegex: false, matchCase: true, out string? error);
        IReadOnlyList<SearchHit> loose = SearchEngine.FindAll(lines, "abc", useRegex: false, matchCase: false, out _);

        Assert.IsNull(error);
        CollectionAssert.AreEqual(new[] { new SearchHit(0, 4, 3) }, exact.ToArray());
        Assert.AreEqual(3, loose.Count);
    }

    [TestMethod]
    public void RegexSearch_ReturnsTheLengthOfEachMatch()
    {
        string[] lines = ["id=12, id=345", "none", "id=6"];

        IReadOnlyList<SearchHit> hits = SearchEngine.FindAll(lines, @"id=\d+", useRegex: true, matchCase: true, out string? error);

        Assert.IsNull(error);
        CollectionAssert.AreEqual(
            new[] { new SearchHit(0, 0, 5), new SearchHit(0, 7, 6), new SearchHit(2, 0, 4) }, hits.ToArray());
    }

    [TestMethod]
    public void RegexSearch_IgnoresEmptyMatches_AndCanIgnoreCase()
    {
        IReadOnlyList<SearchHit> empty = SearchEngine.FindAll(["bbb"], "a*", true, true, out _);
        IReadOnlyList<SearchHit> cased = SearchEngine.FindAll(["Hello HELLO"], "hello", true, false, out _);

        Assert.AreEqual(0, empty.Count);
        Assert.AreEqual(2, cased.Count);
    }

    [TestMethod]
    public void ABadPattern_GivesAnErrorAndNoHits_InsteadOfThrowing()
    {
        IReadOnlyList<SearchHit> hits = SearchEngine.FindAll(["abc"], "(unclosed", true, true, out string? error);

        Assert.AreEqual(0, hits.Count);
        Assert.IsFalse(string.IsNullOrEmpty(error));
    }

    [TestMethod]
    public void APatternThatTakesForever_IsGivenUpOn()
    {
        string evil = new string('a', 40) + "!";

        IReadOnlyList<SearchHit> hits = SearchEngine.FindAll([evil], "(a+)+$", true, true, out string? error);

        Assert.AreEqual(0, hits.Count);
        Assert.IsNotNull(error);
        StringAssertContains(error!, "too long");
    }

    [TestMethod]
    public void NothingToSearchFor_FindsNothing()
    {
        Assert.AreEqual(0, SearchEngine.FindAll(["a"], string.Empty, true, true, out string? error).Count);
        Assert.IsNull(error);
    }

    [TestMethod]
    public void TheNumberOfHits_IsCapped()
    {
        string[] lines = [.. Enumerable.Repeat(new string('a', 1000), 200)];

        IReadOnlyList<SearchHit> hits = SearchEngine.FindAll(lines, "a", false, true, out _);

        Assert.AreEqual(SearchEngine.MaxHits, hits.Count);
    }

    [TestMethod]
    public void Replace_UsesGroupsOnlyForARegularExpression()
    {
        Assert.AreEqual("b-a", SearchEngine.Replace("a-b", @"(\w)-(\w)", "$2-$1", true, true, out int regexCount));
        Assert.AreEqual(1, regexCount);
        Assert.AreEqual("$2-$1", SearchEngine.Replace("a-b", "a-b", "$2-$1", false, true, out int plainCount));
        Assert.AreEqual(1, plainCount);
        Assert.AreEqual("xx", SearchEngine.Replace("ab", "[ab]", "x", true, true, out _));
        Assert.AreEqual("same", SearchEngine.Replace("same", "z*", "!", true, true, out int none), "empty matches change nothing");
        Assert.AreEqual(0, none);
    }

    // ---- the search model and what is highlighted ----

    [TestMethod]
    public void TheSearchModel_RemembersHowLongEachHitIs()
    {
        var model = new SearchModel { Search = "x" };

        model.SetHits([new SearchHit(2, 5, 7), new SearchHit(3, 0, 2)]);

        Assert.AreEqual(7, model.LengthAt(new Position(5, 2)));
        Assert.AreEqual(2, model.LengthAt(new Position(0, 3)));
        Assert.AreEqual(1, model.LengthAt(new Position(9, 9)), "an unknown hit is as long as the plain search text");
        Assert.AreEqual(2, model.Hits.Count);

        model.SetHits([]);
        Assert.AreEqual(0, model.Hits.Count);
    }

    [TestMethod]
    public void TheViewer_HighlightsARegexMatchForItsWholeLength()
    {
        TextEditorControl editor = NewEditor(30, 5, "x", "id=12345 end");
        editor.SearchModel.Search = @"id=\d+";
        editor.SearchModel.SetHits(SearchEngine.FindAll(editor.TextModel.Lines, @"id=\d+", true, true, out _));
        ColorScheme scheme = new();

        ScreenBuffer screen = Draw(editor);

        Assert.AreEqual(scheme.SelectedFocusedBackground, screen.BackgroundAt(0, 1));
        Assert.AreEqual(scheme.SelectedFocusedBackground, screen.BackgroundAt(7, 1), "the last digit is inside the match");
        Assert.AreNotEqual(scheme.SelectedFocusedBackground, screen.BackgroundAt(9, 1), "the text after it is not");
    }

    // ---- the dialog and the replace ----

    private static (SearchTextDialog Dialog, SearchModel Model, ScrollableModel Scroll) NewSearchDialog(params string[] lines)
    {
        var text = new TextModel { Lines = [.. lines] };
        var model = new SearchModel();
        var scroll = new ScrollableModel();
        var dialog = new SearchTextDialog(model, text, scroll, new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, Theme());
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        root.AddControl(dialog);
        dialog.OnInitializeLayout();
        return (dialog, model, scroll);
    }

    private static T Field<T>(object owner, string name)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        Type type = owner.GetType();
        object? value = type.GetField(name, flags)?.GetValue(owner) ?? type.GetProperty(name, flags)?.GetValue(owner);
        return (T)value!;
    }

    [TestMethod]
    public async Task TheSearchDialog_WithRegexOn_FindsByPattern_AndKeepsMatchCaseOnByDefault()
    {
        var (dialog, model, _) = NewSearchDialog("id=1", "ID=22", "other");
        Field<TextBox>(dialog, "_textBoxSearch").Text = @"id=\d+";
        Assert.IsTrue(dialog.MatchCase);
        Assert.IsFalse(dialog.UseRegex);
        Field<CheckBox>(dialog, "_regexBox").IsChecked = true;

        await Field<iConsole.Controls.Buttons.Button>(dialog, "_searchTextButton").TriggerHotKeyAsync();

        Assert.IsTrue(model.UseRegex);
        Assert.AreEqual(1, model.Hits.Count, "match case is on, so ID=22 is not a hit");
        Assert.AreEqual(4, model.LengthAt(model.Hits.First()));
    }

    [TestMethod]
    public async Task TheSearchDialog_WithABadPattern_StaysOpenAndSaysWhy()
    {
        var (dialog, model, _) = NewSearchDialog("abc");
        Field<TextBox>(dialog, "_textBoxSearch").Text = "(open";
        Field<CheckBox>(dialog, "_regexBox").IsChecked = true;

        await Field<iConsole.Controls.Buttons.Button>(dialog, "_searchTextButton").TriggerHotKeyAsync();

        Assert.AreEqual(0, model.Hits.Count);
        Assert.IsTrue(dialog.Title.StartsWith("Search text: "), dialog.Title);
        Assert.IsTrue(dialog.Parent!.Controls.AsSnapshot().Contains(dialog), "the dialog was not closed");
    }

    [TestMethod]
    public async Task TheSearchDialog_PlainSearchStillWorks()
    {
        var (dialog, model, scroll) = NewSearchDialog("one", "two three", "four");
        Field<TextBox>(dialog, "_textBoxSearch").Text = "three";

        await Field<iConsole.Controls.Buttons.Button>(dialog, "_searchTextButton").TriggerHotKeyAsync();

        Assert.AreEqual(1, model.Hits.Count);
        Assert.AreEqual("4,1", $"{model.Hits.First().X},{model.Hits.First().Y}");
        Assert.AreEqual(1, model.Hits.Count, $"cursor was {scroll.CursorPosition.X},{scroll.CursorPosition.Y}");
    }

    [TestMethod]
    public void ReplaceAll_WithARegularExpression_UsesGroupsAsOneUndoStep()
    {
        TextEditorControl editor = NewEditor(40, 6, "first=1", "second=2", "none");

        int count = editor.ReplaceAll(@"(\w+)=(\d)", "$2:$1", matchCase: true, useRegex: true);

        Assert.AreEqual(2, count);
        CollectionAssert.AreEqual(new[] { "1:first", "2:second", "none" }, editor.TextModel.Lines.ToArray());
        editor.Undo();
        CollectionAssert.AreEqual(new[] { "first=1", "second=2", "none" }, editor.TextModel.Lines.ToArray());
    }

    [TestMethod]
    public void ReplaceNext_WithARegularExpression_ReplacesTheMatchAfterTheCursorAndMovesPastIt()
    {
        TextEditorControl editor = NewEditor(40, 6, "a1 b22 c333");
        editor.ShowLine(0, 3);

        bool replaced = editor.ReplaceNext(@"[a-z]\d+", "<$0>", matchCase: true, useRegex: true);

        Assert.IsTrue(replaced);
        Assert.AreEqual("a1 <b22> c333", editor.TextModel.Lines[0]);
        Assert.AreEqual(8, editor.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public void RegexReplace_WithABadPatternOrNoMatch_ChangesNothing()
    {
        TextEditorControl editor = NewEditor(40, 6, "abc");

        Assert.AreEqual(0, editor.ReplaceAll("(bad", "x", true, true));
        Assert.IsFalse(editor.ReplaceNext("(bad", "x", true, true));
        Assert.IsFalse(editor.ReplaceNext(@"\d+", "x", true, true));
        Assert.AreEqual("abc", editor.TextModel.Lines[0]);
    }

    [TestMethod]
    public void ThePlainReplace_IsUnchanged()
    {
        TextEditorControl editor = NewEditor(40, 6, "one two one");

        Assert.AreEqual(2, editor.ReplaceAll("one", "1"));
        Assert.AreEqual("1 two 1", editor.TextModel.Lines[0]);
        Assert.IsTrue(editor.ReplaceNext("two", "$1"));
        Assert.AreEqual("1 $1 1", editor.TextModel.Lines[0]);
    }

    [TestMethod]
    public void TheReplaceDialog_PassesTheRegexChoiceOn_AndRemembersIt()
    {
        TextEditorControl editor = NewEditor(40, 6, "k=1", "k=2");
        var dialog = new ReplaceTextDialog(editor, new Mock<IBeepService>().Object, new Mock<IDateTimeProviderService>().Object, Theme());
        Field<TextBox>(dialog, "_findBox").Text = @"k=(\d)";
        Field<TextBox>(dialog, "_replaceBox").Text = "v:$1";
        Field<CheckBox>(dialog, "_regexBox").IsChecked = true;

        typeof(ReplaceTextDialog).GetMethod("Replace", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [true]);

        Assert.AreEqual(2, dialog.ReplacedCount);
        Assert.IsTrue(dialog.UseRegex);
        Assert.IsTrue(editor.LastUseRegex);
        CollectionAssert.AreEqual(new[] { "v:1", "v:2" }, editor.TextModel.Lines.ToArray());
    }

    private static void StringAssertContains(string actual, string expected) =>
        Assert.IsTrue(actual.Contains(expected, StringComparison.Ordinal), $"[{actual}] lacks [{expected}]");
}
