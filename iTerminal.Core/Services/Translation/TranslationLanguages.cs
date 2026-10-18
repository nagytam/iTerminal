namespace iTerminal.Core.Services.Translation;

/// <summary>A language the translator offers: its ISO 639-1 code (the one LibreTranslate uses) and its English name.</summary>
public sealed record TranslationLanguage(string Code, string Name);

/// <summary>The languages the translator offers. The code is what both engines are given; the name is what the person reads.</summary>
public static class TranslationLanguages
{
    /// <summary>The code that stands for "work it out from the text".</summary>
    public const string Auto = "auto";

    public static IReadOnlyList<TranslationLanguage> All { get; } = ByName(
    [
        new("ar", "Arabic"),
        new("bg", "Bulgarian"),
        new("bn", "Bengali"),
        new("ca", "Catalan"),
        new("cs", "Czech"),
        new("da", "Danish"),
        new("de", "German"),
        new("el", "Greek"),
        new("en", "English"),
        new("eo", "Esperanto"),
        new("es", "Spanish"),
        new("et", "Estonian"),
        new("fa", "Persian"),
        new("fi", "Finnish"),
        new("fr", "French"),
        new("ga", "Irish"),
        new("he", "Hebrew"),
        new("hi", "Hindi"),
        new("hr", "Croatian"),
        new("hu", "Hungarian"),
        new("id", "Indonesian"),
        new("it", "Italian"),
        new("ja", "Japanese"),
        new("ko", "Korean"),
        new("lt", "Lithuanian"),
        new("lv", "Latvian"),
        new("ms", "Malay"),
        new("nb", "Norwegian"),
        new("nl", "Dutch"),
        new("pl", "Polish"),
        new("pt", "Portuguese"),
        new("ro", "Romanian"),
        new("ru", "Russian"),
        new("sk", "Slovak"),
        new("sl", "Slovenian"),
        new("sr", "Serbian"),
        new("sv", "Swedish"),
        new("th", "Thai"),
        new("tr", "Turkish"),
        new("uk", "Ukrainian"),
        new("vi", "Vietnamese"),
        new("zh", "Chinese"),
    ]);

    // The lists in the dialog are read by name, not by code.
    private static List<TranslationLanguage> ByName(IEnumerable<TranslationLanguage> languages) =>
        [.. languages.OrderBy(language => language.Name, StringComparer.Ordinal)];

    /// <summary>The language for a code, or null when the code is not one the translator offers. Regional forms ("pt-BR", "zh_CN") and "no" match their base language.</summary>
    public static TranslationLanguage? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        string normalized = code.Trim().ToLowerInvariant();
        int separator = normalized.IndexOfAny(['-', '_']);
        if (separator > 0)
        {
            normalized = normalized[..separator];
        }

        if (normalized is "no" or "nn")
        {
            normalized = "nb";
        }

        return All.FirstOrDefault(language => language.Code == normalized);
    }

    /// <summary>The English name for a code, or the code itself when it is not one of the offered languages.</summary>
    public static string NameOf(string? code) => Find(code)?.Name ?? code ?? string.Empty;
}
