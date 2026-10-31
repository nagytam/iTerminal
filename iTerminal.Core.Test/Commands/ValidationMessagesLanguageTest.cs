using System.Text.RegularExpressions;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The validation and info messages of the rename, delete, mark, link, file association, labels, format, properties and metadata dialogs follow the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class ValidationMessagesLanguageTest
{
    private static readonly string[] Keys = ["Msg.NoPanelDelete","Msg.TypeNewName","Msg.ReadingLink","Msg.SymlinkFolder","Msg.SymlinkFile","Msg.HardLinkNames","Msg.TypeCommand","Msg.SayWhichFiles","Msg.BlankTagsRemove","Msg.Saving","Msg.Drive","Msg.DriveErased","Msg.Formatting","Msg.OneTagAtATime","Msg.TypePattern","Msg.NoPanelMark","Msg.ErrorPrefix","Msg.ErrorDate","Msg.ErrorMode","Msg.TextOnlyJpegPng"];

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
                if (!(language.Code == "es" && key == "Msg.ErrorPrefix"))
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
        Assert.AreEqual("Drive C:", string.Format(UiText.Get("Msg.Drive", "Drive {0}"), "C:"));

        Speak("de");
        Assert.AreEqual("Laufwerk C:", string.Format(UiText.Get("Msg.Drive", "Drive {0}"), "C:"));
    }
}
