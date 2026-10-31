using System.Text.RegularExpressions;
using iConsole.Localization;

namespace iConsole.Test.Localization;

/// <summary>The JSON viewer status line follows the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class JsonViewerLanguageTest
{
    private static readonly string[] Keys = ["Json.TopLevelItems"];

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
                Assert.AreNotEqual(englishText, text, language.Code + " " + key);

                CollectionAssert.AreEquivalent(Placeholders(englishText), Placeholders(text), language.Code + " " + key);
            }
        }
    }
}
