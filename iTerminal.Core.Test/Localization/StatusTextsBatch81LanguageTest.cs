using System.Resources;
using iConsole.Localization;
using iTerminal.Core.Dialogs.Remote;

namespace iTerminal.Core.Test.Localization;

/// <summary>
/// The status lines of the remote, disk, mail, access control and file diff dialogs follow the chosen language, with their
/// arguments filled in, and the English they fall back to is unchanged. The language is a static, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class StatusTextsBatch81LanguageTest
{
    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static void Speak(string code)
    {
        Localizer localizer = new(
        [
            new ResourceLocalizationSource(new ResourceManager("iConsole.Localization.Resources.Strings", typeof(Localizer).Assembly)),
            new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(RemoteConnectionManagerDialog).Assembly)),
        ]);
        Assert.IsTrue(localizer.SetLanguage(code), code);
        UiText.Current = localizer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheGermanStatusLinesFillTheirArguments()
    {
        Speak("de");

        Assert.AreEqual("Verbindung zu Werk-NAS wird hergestellt…", UiText.Get("Remote.Status.Connecting", string.Empty).Replace("{0}", "Werk-NAS"));
        Assert.AreEqual("Gesendet an anna@example.org.", string.Format(UiText.Get("Mail.Status.Sent", string.Empty), "anna@example.org"));
        Assert.AreEqual("Die letzte Anfrage läuft noch.", UiText.Get("Common.StillWorking", string.Empty));
        Assert.AreEqual("Ein Speicherlaufwerk gehört zum Programm: es gibt nichts zum Auswerfen.", UiText.Get("Disk.Status.EjectMemory", string.Empty));
        Assert.AreEqual("Wird gelesen…", UiText.Get("FileDiff.Status.Reading", string.Empty));
        Assert.AreEqual("Fehler: Zugriff verweigert", string.Format(UiText.Get("Access.Status.Error", string.Empty), "Zugriff verweigert"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithNoLanguageChosenTheEnglishFallbackIsUnchanged()
    {
        Assert.AreEqual("Ejecting E:…", string.Format(UiText.Get("Disk.Status.Ejecting", "Ejecting {0}…"), "E:"));
        Assert.AreEqual("Still working on the last request.", UiText.Get("Common.StillWorking", "Still working on the last request."));
        Assert.AreEqual("Press D again to delete \"Invoice\" from the server.", string.Format(UiText.Get("Mail.Status.PressAgainDelete", "Press D again to delete \"{0}\" from the server."), "Invoice"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesTheStatusLinesThatAreNotEnglish()
    {
        string[] keys =
        [
            "Remote.Status.NoConnection", "Remote.Status.ShellRunning", "Remote.Status.Connecting", "Remote.Status.SessionEnded",
            "Remote.Status.BrowseProtocols", "Remote.Status.PortRange", "Remote.Status.ConfirmDelete", "Remote.Status.CannotStart",
            "Disk.Status.EjectNotHere", "Disk.Status.ChooseEject", "Disk.Status.EjectMemory", "Disk.Status.Ejecting",
            "Disk.Status.FormatNotHere", "Disk.Status.ChooseFormat", "Disk.Status.FormatMemory", "Common.StillWorking",
            "Mail.Status.InboxEmpty", "Mail.Status.PressAgainDelete", "Mail.Status.NeedRecipient", "Mail.Status.NobodyMatches",
            "Mail.Status.SetUpAccount", "Mail.Status.NeedServer",
            "Access.Status.Working", "Access.Status.Inherited", "Access.Status.ConfirmRemove", "Access.Status.OwnerLocked",
            "FileDiff.Status.Reading", "FileDiff.Status.CouldNotRead",
        ];
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Speak("en");
            Dictionary<string, string> english = keys.ToDictionary(k => k, k => UiText.Get(k, string.Empty));
            Speak(language.Code);
            foreach (string key in keys)
            {
                string text = UiText.Get(key, string.Empty);
                Assert.IsTrue(text.Length > 0, language.Code + " " + key);
                Assert.AreNotEqual(english[key], text, language.Code + " " + key + " is still English");
            }
        }
    }
}
