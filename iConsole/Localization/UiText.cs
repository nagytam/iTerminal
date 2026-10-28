namespace iConsole.Localization;

/// <summary>
/// The language the framework's own controls speak, for the few places that build their text without being handed an
/// <see cref="ILocalizer"/>: the standard Cancel and Close buttons, a prompt's default answers. The application sets
/// <see cref="Current"/> once when it starts; until it does, and for a key no language has, the English fallback shows.
/// </summary>
public static class UiText
{
    /// <summary>The localizer the application chose; null before it is set, so a control still shows its English text.</summary>
    public static ILocalizer? Current { get; set; }

    public static string Get(string key, string fallback) =>
        Current is { } localizer && localizer.TryGet(key, out string value) && !string.IsNullOrEmpty(value) ? value : fallback;
}
