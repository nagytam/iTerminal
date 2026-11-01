using System.Text.RegularExpressions;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The encyclopedia dialog's labels, hints and messages follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class EncyclopediaLanguageTest
{
    private static readonly string[] Keys = ["Menu.Encyclopedia","Encyclopedia.Term","Encyclopedia.TermPlaceholder","Encyclopedia.Source","Encyclopedia.EmptyResults","Encyclopedia.LinkOut","Encyclopedia.TypeTerm","Encyclopedia.Searching","Encyclopedia.SearchFailed","Encyclopedia.Reading","Encyclopedia.ReadFailed","Encyclopedia.NothingToOpen","Encyclopedia.Opened","Encyclopedia.OpenFailed","Encyclopedia.NothingFound","Encyclopedia.Found","Encyclopedia.WholeFrom","Encyclopedia.IntroFrom","Encyclopedia.NoText","Encyclopedia.Results","Encyclopedia.Article","DialogHint.SearchOrRead","DialogHint.WholeArticle","DialogHint.OpenInBrowser"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SortDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    /// <summary>Names that are spelled the same in some languages.</summary>
    private static bool SameWordInThatLanguage(string code, string key) =>
        (key == "Encyclopedia.Source" && code == "fr") ||
        (key == "Encyclopedia.Article" && code == "fr");

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order()];

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryTitle_AndKeepsItsPlaceholders()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                if (!SameWordInThatLanguage(language.Code, key))
                {
                    Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                }

                CollectionAssert.AreEquivalent(Placeholders(englishText), Placeholders(text), language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFormattedMessages_FillTheirPlaceholders_InEveryLanguage()
    {
        UiText.Current = null;
        Assert.AreEqual("Searching Wikipedia…", string.Format(UiText.Get("Encyclopedia.Searching", "Searching {0}…"), "Wikipedia"));

        Speak("de");
        Assert.AreEqual("Wikipedia wird durchsucht…", string.Format(UiText.Get("Encyclopedia.Searching", "?"), "Wikipedia"));
    }
}
