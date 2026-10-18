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
            ViewSize = new Size(_text.Length == 0 ? 0 : _text.Length + 2, 1);
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
        if (_text.Length == 0)
        {
            return;
        }

        Color background = BackgroundFor(_kind, Color.Gray);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, new ConxelString($" {_text} ", Color.Black, background));
    }
}
