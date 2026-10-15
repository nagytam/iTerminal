using iConsole.Data;
using iConsole.Display;
using System.Collections.Generic;

namespace iConsole.Services;

public sealed class NullThemeService : IThemeService
{
    public static readonly NullThemeService Instance = new();

    private NullThemeService()
    {
        var defaultScheme = new ColorScheme
        {
            Background = Color.Black,
            Foreground = Color.White,
            SelectedBackground = Color.DarkGray,
            SelectedForeground = Color.White,
            DisabledBackground = Color.Black,
            DisabledForeground = Color.Gray
        };
        CurrentColorScheme = defaultScheme;
        ColorSchemes = new Dictionary<Theme, ColorScheme>() { { Theme.Default, defaultScheme } };
    }

    public ColorScheme CurrentColorScheme { get; }
    public Dictionary<Theme, ColorScheme> ColorSchemes { get; }
}
