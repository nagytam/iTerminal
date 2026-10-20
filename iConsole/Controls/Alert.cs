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

    /// <summary>
    /// Draws a <c>×</c> in the last column; a click on it, or Esc while the alert has focus, clears the
    /// message and raises <see cref="Dismissed"/>. Off by default, so existing alerts keep their width.
    /// </summary>
    public bool IsDismissible
    {
        get => _isDismissible;
        set
        {
            _isDismissible = value;
            CanFocus = value;
            StateHasChanged();
        }
    }

    private bool _isDismissible;

    public event EventHandler? Dismissed;

    public void Dismiss()
    {
        if (_text.Length > 0)
        {
            Text = string.Empty;
            Dismissed?.Invoke(this, EventArgs.Empty);
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (!isHandled && _isDismissible && IsFocused && IsEnabled && _text.Length > 0 && keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            Dismiss();
            return true;
        }

        return isHandled;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (_isDismissible && IsEnabled && _text.Length > 0 && mouseEvent.LeftClicked
            && mouseEvent.Position.Y == AbsolutePosition.Y
            && mouseEvent.Position.X == AbsolutePosition.X + ViewSize.Width - 1)
        {
            Dismiss();
            return true;
        }

        return isHandled;
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
        int messageWidth = width - 2 - (_isDismissible ? 2 : 0);
        if (messageWidth > 0)
        {
            string message = _text.Length > messageWidth ? _text.Cut(messageWidth) : _text;
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + 2, AbsolutePosition.Y), new Size((uint)messageWidth, 1), new ConxelString(message.ExpandRight(messageWidth), colorScheme.Foreground, colorScheme.Background));
        }

        if (_isDismissible && width > 2)
        {
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + width - 2, AbsolutePosition.Y), new Size(2, 1),
                new ConxelString(" ×", IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.DisabledForeground, colorScheme.Background));
        }
    }
}
