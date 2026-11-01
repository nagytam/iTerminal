using System.Reflection;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.Editors;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Localization;

/// <summary>The editor's own dialogs (search, replace, unsaved changes, help) speak the chosen language. The language is a static, so this runs on its own.</summary>
[TestClass]
[DoNotParallelize]
public class EditorDialogsLanguageTest
{
    private static readonly string[] Keys = ["GoToLine", "Line", "GoToOffset", "Offset", "ReplaceText", "Find", "With", "MatchCase", "RegularExpression", "Replace", "ReplaceAll", "SearchText", "Search", "UnsavedChanges", "Discard"];

    private static readonly HashSet<(string Language, string Key)> SameWord = [("de", "Offset"), ("it", "Offset")];

    [TestCleanup]
    public void Reset() => UiText.Current = null;

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static Localizer Speak(string code)
    {
        Localizer localizer = new();
        Assert.IsTrue(localizer.SetLanguage(code));
        UiText.Current = localizer;
        return localizer;
    }

    private static T Member<T>(object owner, string name)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        return (T)(owner.GetType().GetField(name, flags)?.GetValue(owner) ?? owner.GetType().GetProperty(name, flags)?.GetValue(owner))!;
    }

    private static ReplaceTextDialog Replace() =>
        new(new TextEditorControl(Theme()), Mock.Of<IBeepService>(), new DateTimeProviderService(), Theme());

    [Timeout(60_000)]
    [TestMethod]
    public void WithEnglishChosen_TheEditorDialogsKeepTheirExactText()
    {
        Speak("en");
        ReplaceTextDialog replace = Replace();
        UnsavedChangesDialog unsaved = new("a.txt", Theme());

        Assert.AreEqual("Replace text", replace.Title);
        Assert.AreEqual("Find", Member<TextBox>(replace, "_findBox").Label);
        Assert.AreEqual("With", Member<TextBox>(replace, "_replaceBox").Label);
        Assert.AreEqual("Match case", Member<CheckBox>(replace, "_matchCaseBox").Label);
        Assert.AreEqual("Regular expression", Member<CheckBox>(replace, "_regexBox").Label);
        Assert.AreEqual("Replace", Member<Button>(replace, "_replaceButton").Title);
        Assert.AreEqual("Replace all", Member<Button>(replace, "_replaceAllButton").Title);
        Assert.AreEqual("Unsaved changes", unsaved.Title);
        Assert.AreEqual("Save (S)", Member<Button>(unsaved, "_saveButton").Title);
        Assert.AreEqual("Discard (D)", Member<Button>(unsaved, "_discardButton").Title);
        Assert.AreEqual("Cancel (C)", Member<Button>(unsaved, "_cancelButton").Title);
        Assert.AreEqual("Help", new HelpDialog(Theme()).Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InGerman_TheEditorDialogsAreTranslated_AndTheHotKeysStay()
    {
        Speak("de");
        ReplaceTextDialog replace = Replace();
        UnsavedChangesDialog unsaved = new("a.txt", Theme());

        Assert.AreEqual("Text ersetzen", replace.Title);
        Assert.AreEqual("Suchen", Member<TextBox>(replace, "_findBox").Label);
        Assert.AreEqual("Ersetzen durch", Member<TextBox>(replace, "_replaceBox").Label);
        Assert.AreEqual("Regulärer Ausdruck", Member<CheckBox>(replace, "_regexBox").Label);
        Assert.AreEqual("Alle ersetzen", Member<Button>(replace, "_replaceAllButton").Title);
        Assert.AreEqual('A', Member<Button>(replace, "_replaceAllButton").HotKeyChar);
        Assert.AreEqual("Nicht gespeicherte Änderungen", unsaved.Title);
        Assert.AreEqual("Speichern (S)", Member<Button>(unsaved, "_saveButton").Title);
        Assert.AreEqual("Verwerfen (D)", Member<Button>(unsaved, "_discardButton").Title);
        Assert.AreEqual("Abbrechen (C)", Member<Button>(unsaved, "_cancelButton").Title);
        Assert.AreEqual("Hilfe", new HelpDialog(Theme()).Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void EveryLanguage_TranslatesEveryEditorString()
    {
        Localizer english = Speak("en");
        foreach (LanguageInfo language in Localizer.SupportedLanguages.Where(l => l.Code != "en"))
        {
            Localizer localizer = Speak(language.Code);
            foreach (string key in Keys)
            {
                Assert.IsTrue(localizer.TryGet("Editor." + key, out string text), key);
                Assert.IsTrue(english.TryGet("Editor." + key, out string englishText), key);
                if (!SameWord.Contains((language.Code, key)))
                {
                    Assert.AreNotEqual(englishText, text, language.Code + " " + key);
                }
            }
        }
    }
}
