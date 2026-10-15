using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.Tabs;

public class TabPage : Control
{
    public TabPage(string name, IThemeService themeService)
        : base(themeService)
    {
        Name = name;
    }

    public void GetColor(out Color foregroundColor, out Color backgroundColor)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        if (IsEnabled && IsFocused)
        {
            foregroundColor = colorScheme.SelectedFocusedForeground;
            backgroundColor = colorScheme.SelectedBackground;
        }
        else if (IsEnabled)
        {
            foregroundColor = colorScheme.Foreground;
            backgroundColor = colorScheme.Background;
        }
        else
        {
            foregroundColor = colorScheme.DisabledForeground;
            backgroundColor = colorScheme.DisabledBackground;
        }
    }
}
