using System.Text.RegularExpressions;
using iConsole.Controls;
using iConsole.Localization;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Localization;

/// <summary>The pager caption, the error row, the status-bar texts, the wizard header, the help and splash lines, the XML node count and the tab hints follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class ControlTextsBatch68LanguageTest
{
    private static readonly string[] Keys = ["Pagination.Caption","Msg.ErrorPrefix","Hint.OpenMenuNamed","Status.Tasks","Status.Search","Status.Hit","Wizard.Step","Help.FocusedControl","Help.FocusedControlNone","Xml.NodeCount","Splash.SystemInfo","Hint.SwitchTabPage","Hint.SwitchTabPageArrows","Hint.JumpTabPageByNumber"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().Order()];

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryText_AndKeepsItsPlaceholders()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                if (!(key == "Msg.ErrorPrefix" && language.Code == "es"))
                {
                    Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                }

                CollectionAssert.AreEquivalent(Placeholders(englishText), Placeholders(text), language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePagerCaption_FollowsTheLanguage_AndFallsBackToEnglish()
    {
        UiText.Current = null;
        Pagination pager = new(Mock.Of<IThemeService>()) { TotalItems = 50, PageSize = 10 };
        Assert.AreEqual("Page 1 of 5", pager.Caption);

        Speak("de");
        Assert.AreEqual("Seite 1 von 5", pager.Caption);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ThePageCount_DoesNotOverflow_ForAHugeTotal()
    {
        Pagination pager = new(Mock.Of<IThemeService>()) { PageSize = 10, TotalItems = int.MaxValue };
        Assert.AreEqual(214748365, pager.PageCount);

        pager.Page = int.MaxValue;
        Assert.AreEqual(214748364, pager.Page);
    }
}
