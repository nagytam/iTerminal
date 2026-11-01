using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using iConsole.Localization;

namespace iTerminal.Core.Test.Localization;

/// <summary>
/// The shipped <c>.resx</c> files: every key of the English file is in every language, with the same placeholders.
/// A string added in English alone fails here, instead of showing up in English in the middle of a German menu.
/// </summary>
[TestClass]
public class LocalizationResourcesTest
{
    private const string AppResources = "iTerminal.Core.Localization.Resources.AppStrings";

    private static readonly string[] Codes = [.. Localizer.SupportedLanguages.Select(l => l.Code).Where(c => c != Localizer.NeutralCode)];

    public static IEnumerable<object[]> Files =>
    [
        ["iConsole.Localization.Resources.Strings", typeof(Localizer).Assembly],
        [AppResources, typeof(iTerminal.Core.Data.Settings).Assembly],
    ];

    private static Dictionary<string, string> Strings(string baseName, Assembly assembly, string code)
    {
        ResourceManager resources = new(baseName, assembly);
        CultureInfo culture = code == Localizer.NeutralCode ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(code);
        ResourceSet? set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.IsNotNull(set, $"{baseName} has no {code} resources");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static int[] Placeholders(string text) =>
        [.. Regex.Matches(text, @"\{(\d+)").Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order()];

    [Timeout(60_000)]
    [TestMethod]
    [DynamicData(nameof(Files))]
    public void EveryEnglishKeyIsTranslatedIntoEveryLanguage(string baseName, Assembly assembly)
    {
        Dictionary<string, string> english = Strings(baseName, assembly, Localizer.NeutralCode);
        Assert.IsTrue(english.Count > 0);

        foreach (string code in Codes)
        {
            Dictionary<string, string> translated = Strings(baseName, assembly, code);

            string[] missing = [.. english.Keys.Where(k => !translated.TryGetValue(k, out string? t) || string.IsNullOrWhiteSpace(t))];
            Assert.AreEqual(0, missing.Length, $"{baseName}.{code} lacks: {string.Join(", ", missing)}");

            string[] extra = [.. translated.Keys.Except(english.Keys)];
            Assert.AreEqual(0, extra.Length, $"{baseName}.{code} has keys English does not: {string.Join(", ", extra)}");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    [DynamicData(nameof(Files))]
    public void EveryTranslationKeepsThePlaceholdersOfTheEnglishText(string baseName, Assembly assembly)
    {
        Dictionary<string, string> english = Strings(baseName, assembly, Localizer.NeutralCode);

        foreach (string code in Codes)
        {
            foreach ((string key, string text) in Strings(baseName, assembly, code))
            {
                CollectionAssert.AreEqual(Placeholders(english[key]), Placeholders(text), $"{baseName}.{code}: placeholders of {key}");
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MenuLabelsMapToTheKeysTheResourcesHold()
    {
        Localizer localizer = new([new ResourceLocalizationSource(new ResourceManager(AppResources, typeof(iTerminal.Core.Data.Settings).Assembly))]);
        string[] labels = ["File", "Edit", "Navigate", "Tools", "Help", "Quit", "Edit With…", "Sort By…", "Copy to Clipboard", "Language…", "iPhone Browser"];

        foreach (string label in labels)
        {
            Assert.IsTrue(localizer.TryGet(iConsole.Controls.MenuItem.LocalizationKey(label), out _), label);
        }

        Assert.AreEqual("Menu.EditWith", iConsole.Controls.MenuItem.LocalizationKey("Edit With…\tCtrl+F4"));
    }
}
