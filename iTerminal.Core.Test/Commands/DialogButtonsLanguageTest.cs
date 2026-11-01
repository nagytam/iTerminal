using System.Reflection;
using iConsole.Controls.Buttons;
using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The action buttons of the main dialogs say their word in the language the user chose. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogButtonsLanguageTest
{
    private static readonly (string Type, string Field, string Key, string English)[] Buttons =
    [
        ("CompressDialog", "_compressButton", "Menu.Compress", "Compress"),
        ("DeleteDialog", "_deleteButton", "Menu.Delete", "Delete"),
        ("ExtractDialog", "_extractButton", "Menu.Extract", "Extract"),
        ("RenameDialog", "_renameButton", "Menu.Rename", "Rename"),
        ("MultiRenameDialog", "_renameButton", "Menu.Rename", "Rename"),
        ("EditLinkDialog", "_saveButton", "Common.Save", "Save"),
        ("FileAssociationDialog", "_saveButton", "Common.Save", "Save"),
        ("FileLabelsDialog", "_applyButton", "Common.Apply", "Apply"),
        ("LabelFilterDialog", "_applyButton", "Common.Apply", "Apply"),
        ("PropertiesDialog", "_applyButton", "Common.Apply", "Apply"),
        ("NetworkBrowserDialog", "_backButton", "Common.Back", "Back"),
        ("CreateLinkDialog", "_createButton", "Button.Create", "Create"),
        ("DiskImageDialog", "_mountButton", "Button.Mount", "Mount"),
        ("DiskImageDialog", "_unmountButton", "Button.Unmount", "Unmount"),
        ("FormatDriveDialog", "_formatButton", "Button.Format", "Format"),
        ("NetworkBrowserDialog", "_connectButton", "Button.Connect", "Connect"),
        ("UnitConverterDialog", "_swapButton", "Button.Swap", "Swap"),
    ];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(RenameDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    private static string TitleOf(string type, string field)
    {
        Type dialogType = typeof(RenameDialog).Assembly.GetExportedTypes().Single(t => t.Name == type);
        Dialog? dialog = DialogHintsWholeTest.Resolve(dialogType, 0) as Dialog;
        Assert.IsNotNull(dialog, type);
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        object? value = dialogType.GetField(field, Flags)?.GetValue(dialog) ?? dialogType.GetProperty(field, Flags)?.GetValue(dialog);
        Assert.IsNotNull(value, type + "." + field);
        return ((Button)value).Title;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithEnglishChosen_EveryButtonKeepsItsExactEnglishText()
    {
        Speak("en");

        foreach ((string type, string field, _, string english) in Buttons)
        {
            Assert.AreEqual(english, TitleOf(type, field), type + "." + field);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InEveryOtherLanguage_EveryButtonIsTranslated()
    {
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);

            foreach ((string type, string field, string key, _) in Buttons)
            {
                Assert.IsTrue(localizer.TryGet(key, out string text), key);
                Assert.AreEqual(text, TitleOf(type, field), language.Code + " " + type + "." + field);
            }
        }
    }
}
