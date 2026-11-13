using iConsole.Localization;
using iConsole.Services;
using iConsole.Controls;
using Moq;

namespace iConsole.Test.Localization;

/// <summary>The key hints of the text, numeric, time, tree, list and viewer controls follow the chosen language.</summary>
[TestClass]
public class EditorControlHintsLanguageTest
{
    private static readonly string[] Keys = ["Hint.ClearField","Hint.CloseNodeOrParent","Hint.CloseSubmenuOrPrevious","Hint.CopySelection","Hint.CutSelection","Hint.DeleteCharacter","Hint.DeleteLine","Hint.MoveLines","Hint.DuplicateLine","Hint.ToggleComment","Hint.TabsToSpaces","Hint.TrimTrailing","Hint.JoinLines","Hint.RemoveDuplicateLines","Hint.RemoveBlankLines","Hint.ShowWhitespace","Hint.EdgeColumn","Hint.DeleteWordAfter","Hint.DeleteWordBefore","Hint.DeleteToStart","Hint.ExpandOrCollapseNode","Hint.FirstOrLastField","Hint.FirstOrLastRow","Hint.FlipSign","Hint.FoldRegion","Hint.GoToMatchingBracket","Hint.GoToFileStartOrEnd","Hint.IndentOutdent","Hint.JumpFirstLastItem","Hint.MarkItemUnderCursor","Hint.MoveHourMinuteSecond","Hint.MoveByWord","Hint.MoveOverMenuItems","Hint.MoveCursorByPage","Hint.NextBookmark","Hint.NextField","Hint.OpenNodeOrChild","Hint.OpenSubmenuOrNext","Hint.PreviousBookmark","Hint.Redo","Hint.RemoveLastDigit","Hint.RunSelectedItem","Hint.ScrollLine","Hint.ScrollPage","Hint.ScrollSideways","Hint.SelectAll","Hint.SelectItemUnderCursor","Hint.SetMinMax","Hint.SetValueZero","Hint.StartOrEndOfText","Hint.StepField","Hint.StepFieldByTen","Hint.StepValue","Hint.StepValueLarge","Hint.ToggleBookmark","Hint.TypeField","Hint.TypeValue","Hint.LineEnding","Hint.MarkAll","Hint.SortLines","Hint.UpperLowerCase"];

    private IDisposable? _language;

    [TestCleanup]
    public void Reset() => _language?.Dispose();

    private Localizer Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        _language?.Dispose();
        _language = UiText.Scope(localizer);
        return localizer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryKey()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                Assert.AreNotEqual(englishText, text, language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheTextBoxHints_FollowTheLanguage_AndFallBackToEnglish()
    {
        _language?.Dispose();
        _language = UiText.Scope(null);
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Mock.Of<IThemeService>());
        Assert.IsTrue(box.GetKeyHints().Any(h => h.Description == "Delete the word before the cursor"));

        Speak("de");
        Assert.IsTrue(box.GetKeyHints().Any(h => h.Description == "Das Wort vor dem Cursor löschen"));
    }
}
