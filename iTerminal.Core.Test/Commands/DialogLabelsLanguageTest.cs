using System.Reflection;
using iConsole.Dialogs;
using iConsole.Localization;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Commands;

/// <summary>The captions of the input fields in the main dialogs are in the language the user chose. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class DialogLabelsLanguageTest
{
    private static readonly (string Type, string Member, string Key, string English)[] Labels =
    [
        ("RenameDialog", "_textBoxName", "Name", "Name"), ("RenameDialog", "_checkBoxOverwrite", "Overwrite", "Overwrite"),
        ("CopyDialog", "TextBoxTarget", "To", "To"), ("CopyDialog", "_checkBoxOverwrite", "Overwrite", "Overwrite"),
        ("DeleteDialog", "_textBoxName", "Name", "Name"), ("EditLinkDialog", "_textBoxTarget", "Target", "Target"),
        ("FileLabelsDialog", "_colour", "Colour", "Colour"), ("LabelFilterDialog", "_colour", "Colour", "Colour"),
        ("LabelFilterDialog", "_tag", "Tag", "Tag"), ("DocumentManagerDialog", "_searchBox", "Search", "Search"),
        ("UnitConverterDialog", "_inputTextBox", "Value", "Value"), ("CurrencyConverterDialog", "_to", "To", "To"),
        ("TranslatorDialog", "_to", "To", "To"),
    ];

    private static readonly HashSet<(string Language, string Key)> SameWord = [("de", "Name"), ("de", "Tag"), ("it", "Tag")];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static Localizer Speak(string code)
    {
        Localizer localizer = new([new ResourceLocalizationSource(new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(RenameDialog).Assembly))]);
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    private static string? LabelOf(string type, string member)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        Type dialogType = typeof(RenameDialog).Assembly.GetExportedTypes().Single(t => t.Name == type);
        Dialog? dialog = DialogHintsWholeTest.Resolve(dialogType, 0) as Dialog;
        Assert.IsNotNull(dialog, type);
        object? value = null;
        for (Type? t = dialogType; t is not null && value is null; t = t.BaseType)
        {
            value = t.GetField(member, Flags)?.GetValue(dialog) ?? t.GetProperty(member, Flags)?.GetValue(dialog);
        }

        Assert.IsNotNull(value, type + "." + member);
        return value.GetType().GetProperty("Label")!.GetValue(value) as string;
    }

    [TestMethod]
    public void WithEnglishChosen_EveryCaptionKeepsItsExactEnglishText()
    {
        Speak("en");

        foreach ((string type, string member, _, string english) in Labels)
        {
            Assert.AreEqual(english, LabelOf(type, member), type + "." + member);
        }
    }

    [TestMethod]
    public void InEveryOtherLanguage_EveryCaptionIsTranslated()
    {
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);

            foreach ((string type, string member, string key, string english) in Labels)
            {
                Assert.IsTrue(localizer.TryGet("Label." + key, out string text), key);
                Assert.AreEqual(text, LabelOf(type, member), language.Code + " " + type + "." + member);
                if (!SameWord.Contains((language.Code, key)))
                {
                    Assert.AreNotEqual(english, text, language.Code + " " + key);
                }
            }
        }
    }
}
