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

        // A 3270-style mainframe: one hue at two intensities. The palette has only two greens
        // (Green and DarkGreen), so text is bright green on black, selections are a black label on a
        // dim green bar, and the focused row is a black label on the bright green bar. Errors stay red
        // so they still stand out from the green text.
        ColorSchemes[Theme.Mainframe] = new ColorScheme()
        {
            Theme = Theme.Mainframe,
            Background = Color.Black,
            Foreground = Color.Green,
            SelectedForeground = Color.Black,
            SelectedBackground = Color.DarkGreen,
            SelectedFocusedForeground = Color.Black,
            SelectedFocusedBackground = Color.Green,
            DisabledForeground = Color.DarkGreen,
            DisabledSelectedForeground = Color.DarkGreen,
            DisabledBackground = Color.Black,
            ButtonBackground = Color.Black,
            DisabledButtonBackground = Color.Black,
            Error = Color.Red,
        };
    }
}
