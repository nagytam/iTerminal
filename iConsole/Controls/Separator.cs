using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

public class Separator(IThemeService themeService) : Control(themeService)
{
    private Orientation _orientation = Orientation.Horizontal;

    /// <summary>Horizontal rule or vertical line; turning it leaves the old line on screen unless the screen is repainted, so it is.</summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (_orientation != value)
            {
                _orientation = value;
                InvalidateTree();
                StateHasChanged();
            }
        }
    }

    /// <summary>
    /// Optional heading set into a horizontal rule (<c>── Advanced ─────</c>), so a form can be
    /// divided into named groups without a <see cref="GroupBox"/>. Ignored by vertical separators.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The text of a horizontal rule <paramref name="width"/> cells wide, with the title (if any) set in.</summary>
    public static string BuildRule(string title, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        string rule = Border.HorizontalSingle;
        if (string.IsNullOrEmpty(title) || width < 7)
        {
            return rule.Repeat(width);
        }

        string label = " " + (title.Length > width - 6 ? title[..(width - 6)] : title) + " ";
        return (rule.Repeat(2) + label + rule.Repeat(width - 2 - label.Length)).PadRight(width);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        if (Orientation == Orientation.Horizontal)
        {
            var line = BuildRule(Title, (int)ViewSize.Width);
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
