using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

public enum BadgeKind
{
    Neutral,
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// A small status pill: the text on a coloured background with one cell of padding either side
/// (<c> PARTIAL </c>). The width follows the text; the colour follows <see cref="Kind"/>.
/// </summary>
public class Badge : Control
{
    private string _text = string.Empty;
    private BadgeKind _kind;

    public Badge(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(2, 1);
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            Size size = new(_text.Length == 0 ? 0 : _text.Length + 2, 1);
            // A shorter text leaves cells outside the new rectangle that nothing else repaints: ask for a repaint from scratch.
            bool shrinks = size.Width < ViewSize.Width;
            ViewSize = size;
            if (shrinks)
            {
                InvalidateTree();
            }

            StateHasChanged();
        }
    }

    public BadgeKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            StateHasChanged();
        }
    }

    /// <summary>Background colour of the pill for <paramref name="kind"/>.</summary>
    public static Color BackgroundFor(BadgeKind kind, Color neutral) => kind switch
    {
        BadgeKind.Info => Color.Cyan,
        BadgeKind.Success => Color.Green,
        BadgeKind.Warning => Color.Yellow,
        BadgeKind.Error => Color.Red,
        _ => neutral,
    };

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        int width = (int)ViewSize.Width;
        if (width <= 0)
        {
            return;
        }

        // The whole rectangle is painted: a badge whose text got shorter, or empty, used to leave the old text behind.
        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        ConxelString line = _text.Length == 0
            ? new ConxelString(string.Empty, colorScheme.Foreground, colorScheme.Background)
            : new ConxelString($" {_text} ", Color.Black, BackgroundFor(_kind, Color.Gray));
        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
