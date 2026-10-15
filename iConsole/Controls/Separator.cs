using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

public class Separator(IThemeService themeService) : Control(themeService)
{
    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        if (Orientation == Orientation.Horizontal)
        {
            var line = Border.HorizontalSingle.Repeat((int)ViewSize.Width);
            var s = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, AbsolutePosition, ViewSize, s);
        }
        else
        {
            for (uint y = 0; y < ViewSize.Height; y++)
            {
                var s = new ConxelString(Border.VerticalSingle, colorScheme.Foreground, colorScheme.Background);
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + y), SizeHelper.Size1x1, s);
            }
        }
    }
}
