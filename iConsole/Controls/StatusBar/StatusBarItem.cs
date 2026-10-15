using iConsole.Buffers;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls.StatusBar;

public abstract class StatusBarItem(IThemeService themeService) : Control(themeService)
{
    public string Text { get; set; } = string.Empty;

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var text = Text.PadRight((int)ViewSize.Width);
        var s = new ConxelString(text, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
    }
}
