using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.MenuBar;

/// <summary>Whether the machine can reach the internet, shown in the top-right toolbar.</summary>
public class OnlineIndicator(InternetConnectivityService connectivityService, IThemeService themeService) : iConsole.Controls.Control(themeService)
{
    private InternetConnectivityService _connectivityService { get; set; } = connectivityService;

    /// <summary>The text to draw, fitted to <paramref name="width"/>.</summary>
    internal string Describe(int width)
    {
        var text = _connectivityService.IsOnline switch
        {
            true => UiText.Get("Indicator.Online", "Online"),
            false => UiText.Get("Indicator.Offline", "Offline"),
            null => UiText.Get("Indicator.Checking", "Checking"),
        };

        return (text.Shrink(width) ?? string.Empty).ExpandRight(width);
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var text = new ConxelString(Describe((int)ViewSize.Width), colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, text);
    }
}
