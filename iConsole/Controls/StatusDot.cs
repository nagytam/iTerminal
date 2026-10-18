using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// An inline state marker: a coloured dot and a word (<c>● Connected</c>, <c>● Offline</c>). Lighter than a
/// <see cref="Badge"/> — no filled background — so it sits in a list row or a sentence-sized line. The dot
/// takes the colour of <see cref="Kind"/> (success green, warning yellow, error red, info cyan, neutral grey),
/// the text the normal foreground. The row is painted whole, so a shorter text leaves nothing behind.
/// </summary>
public class StatusDot : Control
{
    private string _text = string.Empty;
    private BadgeKind _kind = BadgeKind.Neutral;

    public StatusDot(IThemeService themeService) : base(themeService)
    {
        ViewSize = new Size(2, 1);
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
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

    /// <summary>Cells the dot and its text need.</summary>
    public int NaturalWidth => _text.Length == 0 ? 1 : _text.Length + 2;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        ConxelString line = new("●", Badge.BackgroundFor(_kind, colorScheme.DisabledForeground), colorScheme.Background);
        if (_text.Length > 0 && width > 2)
        {
            line.Append(" " + _text.Cut(width - 2), IsEnabled ? colorScheme.Foreground : colorScheme.DisabledForeground, colorScheme.Background);
        }

        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
