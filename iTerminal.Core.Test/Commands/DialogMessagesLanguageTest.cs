using System.Text.RegularExpressions;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The git, stock, document, viewer, tag and workspace messages and titles follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogMessagesLanguageTest
{
    private static readonly string[] Keys = ["Msg.DeleteItems","Msg.CachedTextUnreadable","Msg.NotGitRepo","Msg.CommitCancelled","Msg.NothingToStash","Msg.NothingToAmend","Msg.UpdatingQuotes","Msg.AddingSymbol","Msg.AddedSymbol","Msg.CurrentTimeZone","Dialog.TagsTitle","Dialog.ImageTitle","Dialog.VideoTitle","Dialog.ImageTextTitle","Msg.WorkspaceName","Msg.WorkspaceBackExcept"];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(SortDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    private static string[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().Order()];

    [TestMethod]
    public void EveryLanguage_TranslatesEveryMessage_AndKeepsItsPlaceholders()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), language.Code + " " + key);
                Assert.IsTrue(english.TryGet(key, out string englishText), key);
                if (!(key is "Dialog.TagsTitle" or "Dialog.VideoTitle" && language.Code is "de" or "fr" or "it" or "es" or "hu" && englishText == text))
                {
                    Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                }

                CollectionAssert.AreEquivalent(Placeholders(englishText), Placeholders(text), language.Code + " " + key);
            }
        }
    }

    [TestMethod]
    public void TheFormattedMessages_FillTheirPlaceholders_InEveryLanguage()
    {
        UiText.Current = null;
        Assert.AreEqual("Delete 3 items", string.Format(UiText.Get("Msg.DeleteItems", "Delete {0} items"), 3));

        Speak("de");
        Assert.AreEqual("3 Elemente löschen", string.Format(UiText.Get("Msg.DeleteItems", "Delete {0} items"), 3));
    }
}
