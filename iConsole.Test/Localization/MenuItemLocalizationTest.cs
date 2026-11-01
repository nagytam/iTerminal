using System.Globalization;
using System.Resources;
using iConsole.Controls;
using iConsole.Localization;
using iConsole.Test.Core;

namespace iConsole.Test.Localization;

/// <summary>A menu in the language of the localizer: the label changes, the shortcut after the tab does not.</summary>
[TestClass]
public class MenuItemLocalizationTest
{
    private sealed class Resources(Dictionary<string, string> german) : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) =>
            culture?.TwoLetterISOLanguageName == "de" && german.TryGetValue(name, out string? text) ? text : null;
    }

    private static Localizer CreateLocalizer() =>
        new([new ResourceLocalizationSource(new Resources(new() { ["Menu.File"] = "Datei", ["Menu.Copy"] = "Kopieren" }))]);

    private static MenuItem Menu()
    {
        var theme = new TestThemeService();
        return new MenuItem("File", null,
        [
            new MenuItem("Copy\tF5", null, [], theme),
            new MenuItem("-", null, [], theme),
            new MenuItem("Untranslated\tF9", null, [], theme),
        ], theme);
    }

    private static List<string> Texts(MenuItem menu) =>
        [.. menu.Controls.AsSnapshot().OfType<MenuItem>().Select(m => m.Text)];

    [Timeout(60_000)]
    [TestMethod]
    public void TheLabelsAreTranslatedAndTheShortcutsKept()
    {
        MenuItem menu = Menu();
        Localizer localizer = CreateLocalizer();
        localizer.SetLanguage("de");

        menu.Localize(localizer);

        Assert.AreEqual("Datei", menu.Text);
        Assert.AreEqual("Datei", menu.Title);
        CollectionAssert.AreEqual(new[] { "Kopieren\tF5", "-", "Untranslated\tF9" }, Texts(menu));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BackToEnglishRestoresTheOriginalText()
    {
        MenuItem menu = Menu();
        Localizer localizer = CreateLocalizer();
        localizer.SetLanguage("de");
        menu.Localize(localizer);
        localizer.SetLanguage("en");

        menu.Localize(localizer);

        Assert.AreEqual("File", menu.Text);
        CollectionAssert.AreEqual(new[] { "Copy\tF5", "-", "Untranslated\tF9" }, Texts(menu));
    }
}
