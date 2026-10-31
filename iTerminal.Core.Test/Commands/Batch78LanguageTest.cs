using System.Resources;
using iConsole.Localization;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.ToDo;
using iTerminal.Core.Dialogs.Video;
using iTerminal.Core.Model.Validations;
using iTerminal.Core.Video;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// The text the custom-drawn dialogs draw and the messages they report follow the chosen language, and the translated
/// column headers and labels still fit the widths the layouts give them. The language is a static, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class Batch78LanguageTest
{
    /// <summary>Translations that are the same word in English (a format string with only a placeholder, or a shared word).</summary>
    private static readonly HashSet<string> SameAsEnglish = ["Contacts.FilterInput", "Passwords.Password", "Torrents.Error"];

    private static readonly string[] Keys =
    [
        "ToDo.Hint", "ToDo.Done", "ToDo.Priority", "ToDo.Due", "ToDo.Subject", "ToDo.PriorityHigh", "ToDo.PriorityLow", "ToDo.NoTasks",
        "Prompt.DeleteNamed", "Contacts.FilterInput", "Contacts.FilterHint", "Torrents.Empty", "Torrents.Error", "Torrents.SavedIn",
        "Passwords.CannotOpen", "Passwords.KeyGone", "Passwords.NothingSaved", "Passwords.SavedPassword", "Passwords.Password", "Passwords.Notes",
        "Remote.Empty", "Remote.Runs", "Remote.NewConnection", "Remote.EditConnection", "Mail.Inbox", "Mail.NoMessages", "Mail.From", "Mail.To",
        "Mail.Subject", "Mail.Date", "Mail.Body", "Mail.Search", "Mail.TypeHint", "Mail.PressEnterHint", "Access.Reading", "Access.NoEntries",
        "Access.AddEntry", "Access.ChangeEntry", "Access.ChangeOwner", "Access.Owner", "Access.Group", "Validation.Exists", "Validation.Invalid",
        "Ffmpeg.Missing", "AI.Off", "Terminal.NoCommand", "Screen.Hint", "Startup.Dismiss",
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(ToDoListDialog).Assembly))]);
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
                if (!SameAsEnglish.Contains(key))
                {
                    Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                }
            }
        }
    }

    [TestMethod]
    public void TheColumnsAndLabels_FitTheirWidthsInEveryLanguage()
    {
        foreach (LanguageInfo language in Localizer.SupportedLanguages)
        {
            Localizer localizer = Speak(language.Code);
            Assert.IsTrue(localizer.TryGet("ToDo.Done", out string done) && done.Length <= 5, language.Code + " Done header");
            Assert.IsTrue(localizer.TryGet("ToDo.Priority", out string priority) && priority.Length <= 8, language.Code + " Priority header");
            Assert.IsTrue(localizer.TryGet("ToDo.Due", out string due) && due.Length <= 10, language.Code + " Due header");
            Assert.IsTrue(localizer.TryGet("ToDo.PriorityHigh", out string high) && high.Length <= 8, language.Code + " High");
            Assert.IsTrue(localizer.TryGet("ToDo.PriorityLow", out string low) && low.Length <= 8, language.Code + " Low");
            foreach (string label in new[] { "Mail.From", "Mail.To", "Mail.Subject", "Mail.Date" })
            {
                Assert.IsTrue(localizer.TryGet(label, out string text) && text.Length <= 8, language.Code + " " + label);
            }
        }
    }

    [TestMethod]
    public void TheMessagesThatAreShownAndReported_FollowTheLanguage()
    {
        Speak("de");
        Assert.AreEqual("Eine Datei oder ein Ordner mit diesem Namen existiert bereits.", UrlValidationModel.ExistsMessage);
        Assert.AreEqual("Kein gültiger Name oder Pfad.", UrlValidationModel.InvalidMessage);
        Assert.AreEqual("Noch kein Terminalbefehl ausgeführt.", TerminalTextTabPage.NoCommandText);
        Assert.AreEqual("Enter oder Esc zum Fortfahren", StartupWarningDialog.DismissHint);
        StringAssert.Contains(FfmpegVideoDecoderFactory.MissingMessage, "ffmpeg");
        StringAssert.Contains(ScreenManagerDialog.Hint, "Esc:schließen");
    }

    [TestMethod]
    public void TheEnglishTexts_AreUnchangedWithoutALanguage()
    {
        UiText.Current = null;
        Assert.AreEqual("A file or folder with this name already exists.", UrlValidationModel.ExistsMessage);
        Assert.AreEqual("No terminal command has been run yet.", TerminalTextTabPage.NoCommandText);
        Assert.AreEqual("Enter or Esc to continue", StartupWarningDialog.DismissHint);
    }
}
