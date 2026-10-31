using System.Resources;
using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// The folder selector's buttons, key hints and page descriptions, the create dialogs' titles and the strings of the
/// torrent form, mark dialog, update dialog and date popup follow the chosen language. The language is a static, so this runs on its own.
/// </summary>
[TestClass]
[DoNotParallelize]
public class FolderSelectorBatch76LanguageTest
{
    /// <summary>Keys whose translation is supposed to differ from English. Field.MagnetFileOrUrl is left out: Italian uses the same words.</summary>
    private static readonly string[] Keys =
    [
        "FolderSelector.Select.Connect", "FolderSelector.Select.SpecialFolder", "FolderSelector.Select.Favourite",
        "FolderSelector.Select.Tree", "FolderSelector.Select.Path", "FolderSelector.Select.History", "FolderSelector.Select.Drive",
        "FolderSelector.Hint.Enter", "FolderSelector.Hint.Esc", "FolderSelector.Hint.PageSwitch", "FolderSelector.Hint.JumpPage",
        "FolderSelector.Hint.TabFocusedPage", "FolderSelector.Hint.TabPage", "FolderSelector.Hint.EscPage", "FolderSelector.Hint.Help",
        "FolderSelector.Hint.CompleteHost", "FolderSelector.Hint.ConnectAnywhere", "FolderSelector.Hint.CompletePath",
        "FolderSelector.Hint.GoAnywhere", "FolderSelector.Hint.AddFavourite", "FolderSelector.Hint.RemoveFavourite",
        "Button.SelectFolder", "Button.SelectDrive", "Button.SelectFavourite", "Button.AddToFavourites", "Button.DeleteFavourite", "Button.Go",
        "Dialog.CreateFolder", "Dialog.CreateFile", "Dialog.MarkFiles", "Dialog.UnmarkFiles", "Button.InstallUpdates",
        "Dialog.DateTimeTimeZone", "Dialog.ImageText", "Field.SaveTo", "Dialog.AddTorrent",
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(FolderSelectorDialog).Assembly))]);
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
                Assert.AreNotEqual(englishText, text, language.Code + " " + key);
            }
        }
    }

    [TestMethod]
    public void TheCreateDialogs_TakeTheirTitleFromTheChosenLanguage_AndDrawTheirHotKeyInTheCaption()
    {
        Speak("de");
        var folder = DialogHintsWholeTest.Resolve(typeof(CreateFolderDialog), 0) as Dialog;
        var file = DialogHintsWholeTest.Resolve(typeof(CreateFileDialog), 0) as Dialog;

        Assert.IsNotNull(folder);
        Assert.IsNotNull(file);
        Assert.AreEqual("Ordner erstellen", folder.Title);
        Assert.AreEqual("Datei erstellen", file.Title);
        Assert.AreEqual('e', CreateEntryDialog.CreateButtonHotKey(folder.Title));
    }

    [TestMethod]
    public void EveryFolderSelectorPage_TheHelpAndSelectHints_FollowTheLanguage()
    {
        UiText.Current = null;
        var english = DialogHintsWholeTest.Resolve(typeof(FolderSelectorDrivesTabPage), 0) as FolderSelectorTabPage;
        Assert.IsNotNull(english);
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Show this help."));
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Go to the drive under the cursor."));

        Speak("de");
        var german = DialogHintsWholeTest.Resolve(typeof(FolderSelectorDrivesTabPage), 0) as FolderSelectorTabPage;
        Assert.IsNotNull(german);
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Diese Hilfe anzeigen."));
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Zum Laufwerk unter dem Cursor wechseln."));
    }

    [TestMethod]
    public void TheFolderSelector_HintsFollowTheLanguage()
    {
        UiText.Current = null;
        var english = DialogHintsWholeTest.Resolve(typeof(FolderSelectorDialog), 0) as Dialog;
        Assert.IsNotNull(english);
        Assert.IsTrue(english.GetKeyHints().Any(h => h.Description == "Go to the folder the focused page has under its cursor, and close."));

        Speak("de");
        var german = DialogHintsWholeTest.Resolve(typeof(FolderSelectorDialog), 0) as Dialog;
        Assert.IsNotNull(german);
        Assert.IsTrue(german.GetKeyHints().Any(h => h.Description == "Zum Ordner unter dem Cursor der aktiven Seite wechseln und schließen."));
    }
}
