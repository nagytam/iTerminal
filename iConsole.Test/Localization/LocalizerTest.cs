using System.Globalization;
using System.Resources;
using iConsole.Localization;

namespace iConsole.Test.Localization;

/// <summary>The localizer: the language, the English fallback, format arguments and the saved choice.</summary>
[TestClass]
public class LocalizerTest
{
    /// <summary>A resource set held in memory, so a test controls exactly which language has which key.</summary>
    private sealed class FakeResources(Dictionary<string, Dictionary<string, string>> byLanguage) : ResourceManager
    {
        // The neutral language is the empty-named key.
        public override string? GetString(string name, CultureInfo? culture)
        {
            // English has no satellite of its own: the neutral resources answer for it, as the real manager does.
            string language = culture is null || culture.TwoLetterISOLanguageName is "en" or "iv" ? string.Empty : culture.TwoLetterISOLanguageName;
            return byLanguage.TryGetValue(language, out Dictionary<string, string>? strings) && strings.TryGetValue(name, out string? text)
                ? text
                : null;
        }
    }

    private sealed class MemoryStore(string? saved = null) : ILanguageStore
    {
        public string? Saved { get; private set; } = saved;

        public string? Load() => Saved;

        public void Save(string code) => Saved = code;
    }

    private static Localizer Create(ILanguageStore? store = null) =>
        new(
            [
                new ResourceLocalizationSource(new FakeResources(new()
                {
                    [""] = new() { ["Only.Neutral"] = "neutral only", ["Both"] = "both in English", ["Empty"] = "empty in German", ["Hello"] = "Hello {0}, you have {1} files", ["Broken"] = "broken {0} {1}" },
                    ["de"] = new() { ["Both"] = "beides auf Deutsch", ["Empty"] = "", ["Hello"] = "Hallo {0}, du hast {1} Dateien", ["Broken"] = "kaputt {" },
                })),
            ],
            store);

    [TestMethod]
    public void EnglishIsTheDefault()
    {
        Localizer localizer = Create();

        Assert.AreEqual("en", localizer.LanguageCode);
        Assert.AreEqual("both in English", localizer.Get("Both"));
        Assert.AreEqual("Close", localizer.Get("Common.Close"), "the framework's own strings are always there");
    }

    [TestMethod]
    public void ALanguageWithoutTheKeyShowsTheEnglishText()
    {
        Localizer localizer = Create();
        localizer.SetLanguage("de");

        Assert.AreEqual("beides auf Deutsch", localizer.Get("Both"));
        Assert.AreEqual("neutral only", localizer.Get("Only.Neutral"));
    }

    [TestMethod]
    public void AnEmptyTranslationFallsBackToEnglish()
    {
        Localizer localizer = Create();
        localizer.SetLanguage("de");

        Assert.AreEqual("empty in German", localizer.Get("Empty"));
    }

    [TestMethod]
    public void AKeyNobodyKnowsShowsTheKeyAndTryGetSaysSo()
    {
        Localizer localizer = Create();

        Assert.AreEqual("No.Such.Key", localizer.Get("No.Such.Key"));
        Assert.IsFalse(localizer.TryGet("No.Such.Key", out string value));
        Assert.AreEqual(string.Empty, value);
    }

    [TestMethod]
    public void ALanguageWithNoResourcesAtAllShowsEnglish()
    {
        Localizer localizer = Create();
        localizer.SetLanguage("hu");

        Assert.AreEqual("both in English", localizer.Get("Both"));
    }

    [TestMethod]
    public void ArgumentsAreFormattedInTheCurrentLanguage()
    {
        Localizer localizer = Create();
        Assert.AreEqual("Hello Ann, you have 3 files", localizer.Get("Hello", "Ann", 3));

        localizer.SetLanguage("de");
        Assert.AreEqual("Hallo Ann, du hast 3 Dateien", localizer.Get("Hello", "Ann", 3));
    }

    [TestMethod]
    public void NoArgumentsReturnsTheTextAsItIs()
    {
        Localizer localizer = Create();

        Assert.AreEqual("Hello {0}, you have {1} files", localizer.Get("Hello"));
    }

    [TestMethod]
    public void ABrokenPlaceholderDoesNotThrow()
    {
        Localizer localizer = Create();
        localizer.SetLanguage("de");

        Assert.AreEqual("kaputt {", localizer.Get("Broken", "x"));
        Assert.AreEqual("broken {0} {1}", Create().Get("Broken", "x"), "too few arguments for the placeholders is the same case");
    }

    [TestMethod]
    public void SwitchingLanguageRaisesTheEventOnceAndNotForTheSameLanguage()
    {
        Localizer localizer = Create();
        int raised = 0;
        localizer.LanguageChanged += (_, _) => raised++;

        Assert.IsTrue(localizer.SetLanguage("de"));
        Assert.IsTrue(localizer.SetLanguage("DE"));
        Assert.IsTrue(localizer.SetLanguage("fr"));

        Assert.AreEqual(2, raised);
        Assert.AreEqual("fr", localizer.LanguageCode);
    }

    [TestMethod]
    public void AnUnknownLanguageChangesNothing()
    {
        MemoryStore store = new();
        Localizer localizer = Create(store);
        localizer.SetLanguage("de");
        int raised = 0;
        localizer.LanguageChanged += (_, _) => raised++;

        Assert.IsFalse(localizer.SetLanguage("xx"));
        Assert.IsFalse(localizer.SetLanguage(""));

        Assert.AreEqual("de", localizer.LanguageCode);
        Assert.AreEqual("de", store.Saved);
        Assert.AreEqual(0, raised);
    }

    [TestMethod]
    public void TheChosenLanguageIsSavedAndRestoredByTheNextInstance()
    {
        MemoryStore store = new();
        Create(store).SetLanguage("it");

        Localizer next = Create(store);

        Assert.AreEqual("it", next.LanguageCode);
        Assert.AreEqual("Chiudi", next.Get("Common.Close"));
    }

    [TestMethod]
    public void ASavedLanguageThatIsNotSupportedLeavesEnglish()
    {
        Assert.AreEqual("en", Create(new MemoryStore("klingon")).LanguageCode);
        Assert.AreEqual("en", Create(new MemoryStore("")).LanguageCode);
    }

    [TestMethod]
    public void EveryShippedLanguageIsListedByItsOwnNameEnglishFirst()
    {
        string[] names = [.. Create().Languages.Select(l => l.NativeName)];

        CollectionAssert.AreEqual(new[] { "English", "Deutsch", "Français", "Español", "Italiano", "Magyar" }, names);
    }
}
