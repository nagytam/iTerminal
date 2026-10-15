using iConsole.Data;
using iConsole.Display;

namespace iConsole.Services;

public class ThemeService : IThemeService
{
    public ColorScheme CurrentColorScheme 
    {
        get
        {
            if (ColorSchemes.TryGetValue(Theme, out ColorScheme? value))
            {
                return value;
            }
            else if (ColorSchemes.Count != 0)
            {
                return ColorSchemes.FirstOrDefault().Value;
            }
            return new ColorScheme();
        }
    }
    public Theme Theme { get; set; }

    public bool TrySetTheme(Theme theme)
    {
        if (!ColorSchemes.ContainsKey(theme))
        {
            return false;
        }

        Theme = theme;
        return true;
    }
    public Dictionary<Theme, ColorScheme> ColorSchemes { get; private set; } = [];

    public ThemeService()
    {
        ColorSchemes[Theme.Default] = new ColorScheme();
        ColorSchemes[Theme.Dark] = new ColorScheme()
        {
            Theme = Theme.Dark,
            Background = Color.Black,
            Foreground = Color.White,
        };
    }
}
