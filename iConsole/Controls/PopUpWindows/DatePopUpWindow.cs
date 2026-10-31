using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using System.Runtime.CompilerServices;

namespace iConsole.Controls.PopUpWindows;

public class DatePopUpWindow : PopUpWindow
{
    private readonly IThemeService _themeService;

    public DatePopUpWindow(IThemeService themeService) 
        : base(iConsole.Localization.UiText.Get("Dialog.DateTimeTimeZone", "Date, Time and TimeZone"), themeService)
    {
        _themeService = themeService;
        Hide();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        ViewSize = new Size(80, 7);
        Position = new Position(10, 10);
    }

    public override void OnIdle()
    {
        if (IsVisible)
        {
            StateHasChanged();
        }
        base.OnIdle();
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = _themeService.CurrentColorScheme;

        var now = DateTime.Now;
        var line = ("Date: " + now.ToString("yyyy-MM-dd")).PadRight((int)ViewSize.Width - 2);
        var lineString = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 1), ViewSize, lineString);
        line = ("Time: " + now.ToString("HH:mm:ss")).PadRight((int)ViewSize.Width - 2);
        lineString = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 2), ViewSize, lineString);
        var timeZoneInfo = TimeZoneInfo.Local;
        line = ("Time zone: " + timeZoneInfo.DisplayName).PadRight((int)ViewSize.Width - 2);
        lineString = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 3), ViewSize, lineString);
        var utcNow = DateTime.UtcNow;
        line = ("Date (UTC): " + utcNow.ToString("yyyy-MM-dd")).PadRight((int)ViewSize.Width - 2);
        lineString = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 4), ViewSize, lineString);
        line = ("Time (UTC): " + utcNow.ToString("HH:mm:ss")).PadRight((int)ViewSize.Width - 2);
        lineString = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 5), ViewSize, lineString);

        for (int y = (int)AbsolutePosition.Y + 6; y < AbsolutePosition.Y + ViewSize.Height - 1; y++)
        {
            line = string.Empty.PadRight((int)ViewSize.Width - 2);
            lineString = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X + 1, (uint)y), ViewSize, lineString);
        }
    }
}
