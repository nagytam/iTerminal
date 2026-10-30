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
        ];
        foreach ((Type dialog, string key) in dialogs)
        {
            Assert.IsTrue(localizer.TryGet(key, out string german), key);
            Assert.AreEqual(german, TitleOf(dialog), dialog.Name);
        }

        Assert.AreEqual("Umbenennen", TitleOf(typeof(RenameDialog)));
    }
}
