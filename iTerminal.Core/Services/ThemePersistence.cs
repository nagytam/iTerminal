using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Data;

namespace iTerminal.Core.Services;

/// <summary>
/// Keeps the theme chosen in the theme manager across runs. It used to apply for the session only:
/// every start-up came back in the default colours, whatever had been picked.
/// </summary>
public static class ThemePersistence
{
    /// <summary>Records <paramref name="theme"/> as the one to start with next time.</summary>
    public static void Save(ISettingsService? settingsService, Theme theme) =>
        settingsService?.Set(Settings.Theme, theme.ToString());

    /// <summary>
    /// Applies the saved theme, if there is one the theme service knows. Returns whether a theme was
    /// applied; an unknown or missing value leaves the current theme alone.
    /// </summary>
    public static bool Restore(IThemeService themeService, ISettingsService? settingsService)
    {
        var saved = settingsService?.Get(Settings.Theme);
        return Enum.TryParse<Theme>(saved, ignoreCase: true, out var theme)
            && Enum.IsDefined(theme)
            && themeService.TrySetTheme(theme);
    }
}
