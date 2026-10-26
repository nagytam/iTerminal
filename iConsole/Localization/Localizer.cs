using System.Globalization;
using System.Resources;

namespace iConsole.Localization;

/// <summary>The <see cref="ILocalizer"/> over <c>.resx</c> resources. See the interface for the rules.</summary>
public sealed class Localizer : ILocalizer
{
    /// <summary>The code of the neutral language.</summary>
    public const string NeutralCode = "en";

    /// <summary>The languages the application ships, each by its own name.</summary>
    public static readonly IReadOnlyList<LanguageInfo> SupportedLanguages =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("fr", "Français"),
        new("es", "Español"),
        new("it", "Italiano"),
        new("hu", "Magyar"),
    ];

    private readonly List<ResourceManager> _resources;
    private readonly ILanguageStore? _store;
    private volatile CultureInfo _culture = CultureInfo.GetCultureInfo(NeutralCode);

    public Localizer(IEnumerable<ILocalizationSource>? sources = null, ILanguageStore? store = null)
    {
        _resources =
        [
            new ResourceManager("iConsole.Localization.Resources.Strings", typeof(Localizer).Assembly),
            .. (sources ?? []).Select(source => source.Resources),
        ];
        _store = store;

        // An unknown or missing saved language leaves English, the default.
        string? saved = store?.Load();
        if (Find(saved) is { } language)
        {
            _culture = CultureInfo.GetCultureInfo(language.Code);
        }
    }

    public CultureInfo Culture => _culture;

    public string LanguageCode => _culture.TwoLetterISOLanguageName;

    public IReadOnlyList<LanguageInfo> Languages => SupportedLanguages;

    public event EventHandler? LanguageChanged;

    public bool TryGet(string key, out string value)
    {
        CultureInfo culture = _culture;
        foreach (ResourceManager resources in _resources)
        {
            // A translation that is missing or left empty falls back to the neutral text.
            string? text = Lookup(resources, key, culture);
            if (string.IsNullOrEmpty(text) && culture.TwoLetterISOLanguageName != NeutralCode)
            {
                text = Lookup(resources, key, CultureInfo.InvariantCulture);
            }

            if (!string.IsNullOrEmpty(text))
            {
                value = text;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public string Get(string key, params object?[] args)
    {
        if (!TryGet(key, out string text))
        {
            return key;
        }

        if (args is not { Length: > 0 })
        {
            return text;
        }

        try
        {
            return string.Format(_culture, text, args);
        }
        catch (FormatException)
        {
            // A translation with a broken placeholder must not take the screen down with it.
            return text;
        }
    }

    public bool SetLanguage(string code)
    {
        if (Find(code) is not { } language)
        {
            return false;
        }

        bool changed = !string.Equals(_culture.TwoLetterISOLanguageName, language.Code, StringComparison.Ordinal);
        _culture = CultureInfo.GetCultureInfo(language.Code);
        _store?.Save(language.Code);
        if (changed)
        {
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    private static LanguageInfo? Find(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? null
            : SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? Lookup(ResourceManager resources, string key, CultureInfo culture)
    {
        try
        {
            return resources.GetString(key, culture);
        }
        catch (MissingManifestResourceException)
        {
            return null;
        }
    }
}
