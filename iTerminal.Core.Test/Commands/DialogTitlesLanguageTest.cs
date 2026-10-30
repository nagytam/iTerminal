using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Search;

namespace iTerminal.Core.Test.Commands;

/// <summary>A dialog is titled with the same word its menu entry uses, in the language the user chose. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogTitlesLanguageTest
{
    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static string TitleOf(Type type)
    {
        Dialog? dialog = DialogHintsWholeTest.Resolve(type, 0) as Dialog;
        Assert.IsNotNull(dialog, type.Name);
        return dialog.Title;
    }

    [TestMethod]
    public void WithNoLanguageSet_TheTitlesAreEnglish()
    {
        UiText.Current = null;

        Assert.AreEqual("Rename", TitleOf(typeof(RenameDialog)));
        Assert.AreEqual("Find Files", TitleOf(typeof(FindFilesDialog)));
    }

    [TestMethod]
    public void InGerman_TheMainDialogs_AreTitledLikeTheirMenuEntries()
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(RenameDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage("de"));
        UiText.Current = localizer;

        (Type Dialog, string Key)[] dialogs =
        [
            (typeof(AboutDialog), "Menu.About"), (typeof(CompressDialog), "Menu.Compress"), (typeof(ExtractDialog), "Menu.Extract"),
            (typeof(MarkPatternDialog), "Menu.Mark"), (typeof(PropertiesDialog), "Menu.Properties"), (typeof(RenameDialog), "Menu.Rename"),
            (typeof(FindFilesDialog), "Menu.FindFiles"), (typeof(SettingsDialog), "Menu.Settings"),
            (typeof(ChecksumDialog), "Menu.Checksums"), (typeof(SignatureDialog), "Menu.Signatures"), (typeof(TaskManagerDialog), "Menu.TaskManager"),
            (typeof(CreateLinkDialog), "Dialog.CreateLink"), (typeof(LabelFilterDialog), "Dialog.FilterByLabel"), (typeof(FileLabelsDialog), "Dialog.ColourLabels"),
            (typeof(iTerminal.Core.Dialogs.Workspaces.WorkspaceManagerDialog), "Menu.Workspaces"), (typeof(iTerminal.Core.Dialogs.Translator.TranslatorDialog), "Menu.Translator"),
            (typeof(iTerminal.Core.Dialogs.CurrencyConverter.CurrencyConverterDialog), "Menu.CurrencyConverter"), (typeof(IPhoneBrowserDialog), "Menu.iPhoneBrowser"),
        ];
        foreach ((Type dialog, string key) in dialogs)
        {
            Assert.IsTrue(localizer.TryGet(key, out string german), key);
            Assert.AreEqual(german, TitleOf(dialog), dialog.Name);
        }

        Assert.AreEqual("Umbenennen", TitleOf(typeof(RenameDialog)));
    }

    private static readonly (string Type, string Key, string English)[] Titled =
    [
        ("AccessControlDialog", "Permissions", "Permissions"), ("CalculatorDialog", "Calculator", "Calculator"), ("CalendarDialog", "Calendar", "Calendar"),
        ("CommandPaletteDialog", "Commands", "Commands"), ("DiskUsageDialog", "DiskUsage", "Disk usage"), ("DiskManagerDialog", "DiskManager", "Disk manager"),
        ("DocumentManagerDialog", "Documents", "Documents"), ("EditLinkDialog", "EditLink", "Edit link"), ("SortDialog", "SortBy", "Sort by"),
        ("ProcessManagerDialog", "ProcessManager", "Process manager"), ("NetworkManagerDialog", "NetworkManager", "Network manager"),
        ("SecureWipeDialog", "SecureWipe", "Secure wipe"), ("ThemeManagerDialog", "ThemeManager", "Theme manager"),
    ];

    private static Type Find(string name) => typeof(RenameDialog).Assembly.GetExportedTypes().Single(t => t.Name == name);

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(RenameDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    [TestMethod]
    public void WithEnglishChosen_EveryTitleKeepsItsExactEnglishText()
    {
        Speak("en");

        foreach ((string type, _, string english) in Titled)
        {
            Assert.AreEqual(english, TitleOf(Find(type)), type);
        }
    }

    [TestMethod]
    public void InEveryOtherLanguage_EveryTitleIsTranslated()
    {
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);

            foreach ((string type, string key, string english) in Titled)
            {
                Assert.IsTrue(localizer.TryGet("Dialog." + key, out string text), key);
                if (!(language.Code == "fr" && key == "Documents"))
                {
                    Assert.AreNotEqual(english, text, language.Code + " " + key);
                }

                Assert.AreEqual(text, TitleOf(Find(type)), language.Code + " " + type);
            }
        }
    }
}
