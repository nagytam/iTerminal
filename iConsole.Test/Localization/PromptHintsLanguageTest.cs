using iConsole.Localization;

namespace iConsole.Test.Localization;

/// <summary>The key hints of the prompt dialogs (accept, choose, mark, yes or no, cancel) follow the chosen language.</summary>
[TestClass]
public class PromptHintsLanguageTest
{
    private static readonly string[] Keys = ["Prompt.HintAccept", "Prompt.HintFilterChoose", "Prompt.HintMarkConfirm", "Prompt.HintYes", "Prompt.HintNo", "Prompt.HintCancel"];

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
    public void EveryLanguage_TranslatesEveryPromptHint()
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
    public void GermanPromptHint_SaysAbbrechen()
    {
        Speak("de");
        Assert.AreEqual("Esc: abbrechen", UiText.Get("Prompt.HintCancel", "Esc: cancel"));
    }
}
