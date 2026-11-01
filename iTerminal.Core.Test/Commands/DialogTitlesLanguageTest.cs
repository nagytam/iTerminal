using System.Text.RegularExpressions;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The titles and status messages of the contacts, games, git, stock, settings, task manager, terminal and mail dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogTitlesLanguageTest
{
    private static readonly string[] Keys = ["Dialog.ContactsTitle","Dialog.GamesTitle","Msg.Cancelled","Dialog.GitDetails","Msg.FailedTitle","Msg.Searching","Msg.SearchFailed","Settings.Order","Settings.Listing","Dialog.TaskManagerTitle","Dialog.TerminalTitle","Dialog.TerminalCommandTitle","Dialog.MailAccountTitle"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SortDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    /// <summary>Names such as "Terminal" and "Contacts" are spelled the same in some languages.</summary>
    private static bool SameWordInThatLanguage(string code, string key) =>
        (key == "Dialog.ContactsTitle" && code == "fr") ||
        (key == "Dialog.GitDetails" && code == "de") ||
        (key is "Dialog.TerminalTitle" or "Dialog.TerminalCommandTitle" && code is "de" or "es" or "fr");

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
    public void TheFormattedTitles_FillTheirPlaceholders_InEveryLanguage()
    {
        UiText.Current = null;
        Assert.AreEqual("Task Manager (2)", string.Format(UiText.Get("Dialog.TaskManagerTitle", "Task Manager ({0})"), 2));

        Speak("de");
        Assert.AreEqual("Task-Manager (2)", string.Format(UiText.Get("Dialog.TaskManagerTitle", "Task Manager ({0})"), 2));
    }
}
