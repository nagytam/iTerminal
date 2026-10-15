using iConsole.Data;
using iConsole.Display;

namespace iConsole.Services;

public interface IThemeService
{
    public ColorScheme CurrentColorScheme { get; }
    Dictionary<Theme, ColorScheme> ColorSchemes { get; }

    /// <summary>
    /// Switches the current colour scheme. Returns false when this service cannot switch (the
    /// default — a fixed-scheme service, such as the ones tests use) or does not know the theme.
    /// </summary>
    bool TrySetTheme(Theme theme) => false;
}