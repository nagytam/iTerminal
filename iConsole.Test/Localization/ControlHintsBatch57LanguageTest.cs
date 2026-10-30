using iConsole.Controls;
using iConsole.Localization;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Localization;

/// <summary>The key hints of the remaining controls and the go-to and unsaved-changes dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class ControlHintsBatch57LanguageTest
{
    private static readonly string[] Keys = ["Hint.BackPreviousMenu","Hint.CancelKeepEditing","Hint.CentreDivider","Hint.FirstOrLastSegmentChoose","Hint.ChooseHighlightedItem","Hint.ChoosePreviousNextOption","Hint.ChoosePreviousNextSegment","Hint.ClearRatingOrMax","Hint.ClearSearch","Hint.CloseMenuShort","Hint.CloseList","Hint.ClosePopup","Hint.CloseViewer","Hint.CloseWithoutMoving","Hint.CollapseSection","Hint.DiscardChanges","Hint.ExpandOrCollapseSection","Hint.ExpandSection","Hint.FirstOrLastOption","Hint.FirstOrLastPage","Hint.FollowLink","Hint.GoToTheLine","Hint.GoToTheOffset","Hint.GoTopOrBottom","Hint.GoTopOrEnd","Hint.GoUpFolder","Hint.LowerRaiseRating","Hint.LowerRaiseValue","Hint.MovePageAtATime","Hint.MoveAlongToolbar","Hint.MoveMenuSelection","Hint.MoveDividerBy8","Hint.MoveHighlight","Hint.NewestLineFollow","Hint.OldestLine","Hint.OpenSubmenuRunCommand","Hint.OpenHighlightedRow","Hint.PreviousNextPage","Hint.RaiseLowerLargeStep","Hint.ResetClock","Hint.RunTheItem","Hint.SaveChanges","Hint.Scroll","Hint.ScrollRow","Hint.ScrollOneLine","Hint.ScrollOnePage","Hint.ScrollOneRow","Hint.SelectThisOption","Hint.SetRatingDirectly","Hint.SwitchTextHex","Hint.SwitchSetting","Hint.TypeLineNumber","Hint.TypeOffset"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

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

    [TestMethod]
    public void TheSliderHints_FollowTheLanguage_AndFallBackToEnglish()
    {
        UiText.Current = null;
        Slider slider = new(Mock.Of<IThemeService>());
        Assert.IsTrue(slider.GetKeyHints().Any(h => h.Description == "Set the minimum or maximum"));

        Speak("de");
        Assert.IsTrue(slider.GetKeyHints().Any(h => h.Description == "Das Minimum oder Maximum setzen"));
    }
}
