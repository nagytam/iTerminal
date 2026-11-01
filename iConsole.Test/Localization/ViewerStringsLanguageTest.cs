using System.Text.RegularExpressions;
using iConsole.Localization;

namespace iConsole.Test.Localization;

/// <summary>The JSON and XML viewers and the text editor speak the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class ViewerStringsLanguageTest
{
    private static readonly string[] Keys =
    [
        "NothingToShow", "CouldNotRead", "NotValidJson", "Validating", "ValidJson", "HintJsonValidate", "HintToggleText", "HintExpandAll",
        "XPathPlaceholder", "NoResults", "NotWellFormed", "RunningQuery", "XPathError", "NoMatches", "Matches", "ShowingMatches", "Valid",
        "ErrorsWarnings", "HintXmlValidate", "HintXPathBox", "HintRunXPath", "NotSaved", "NowhereToSave", "HintSave", "HintIndent", "HintCodePage",
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryViewerString()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet("Viewer." + key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet("Viewer." + key, out string englishText), key);
                Assert.AreNotEqual(englishText, text, language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryTranslation_KeepsTheEnglishPlaceholders()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                localizer.TryGet("Viewer." + key, out string text);
                english.TryGet("Viewer." + key, out string englishText);
                CollectionAssert.AreEquivalent(Placeholders(englishText), Placeholders(text), language.Code + " " + key);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutALocalizer_TheFormattedMessagesKeepTheirEnglishText()
    {
        UiText.Current = null;
        Assert.AreEqual("Not valid JSON: x", string.Format(UiText.Get("Viewer.NotValidJson", "Not valid JSON: {0}"), "x"));
        Assert.AreEqual("Showing 1 of 2 matches", string.Format(UiText.Get("Viewer.ShowingMatches", "Showing {0:N0} of {1:N0} matches"), 1, 2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InGerman_TheFormattedMessagesFillTheirPlaceholders()
    {
        Speak("de");
        string text = string.Format(UiText.Get("Viewer.ErrorsWarnings", "{0:N0} error(s), {1:N0} warning(s)"), 3, 1);
        Assert.AreEqual("3 Fehler, 1 Warnung(en)", text);
    }

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)(?::[A-Za-z0-9]+)?\}").Select(m => m.Groups[1].Value).Distinct().Order()];
}
