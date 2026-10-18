using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// One line of feedback with a severity glyph (ℹ ✔ ⚠ ✖) in the severity colour, followed by the message.
/// It paints its whole row even when empty, so a cleared message leaves no stale text behind.
/// </summary>
public class Alert(IThemeService themeService) : Control(themeService)
{
    private string _text = string.Empty;
    private BadgeKind _kind = BadgeKind.Info;

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

    public static char GlyphFor(BadgeKind kind) => kind switch
    {
        BadgeKind.Success => '✔',
        BadgeKind.Warning => '⚠',
        BadgeKind.Error => '✖',
        _ => 'ℹ',
    };

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        int width = (int)ViewSize.Width;
        if (width <= 0)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        if (_text.Length == 0)
        {
            buffer.Write(ZOrder, AbsolutePosition, new Size((uint)width, 1), new ConxelString(new string(' ', width), colorScheme.Foreground, colorScheme.Background));
            return;
        }

        Color accent = Badge.BackgroundFor(_kind, colorScheme.Foreground);
        buffer.Write(ZOrder, AbsolutePosition, new Size(2, 1), new ConxelString(GlyphFor(_kind) + " ", accent, colorScheme.Background));
        if (width > 2)
        {
            string message = _text.Length > width - 2 ? _text.Cut(width - 2) : _text;
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + 2, AbsolutePosition.Y), new Size((uint)(width - 2), 1), new ConxelString(message.ExpandRight(width - 2), colorScheme.Foreground, colorScheme.Background));
        }
    }
}
