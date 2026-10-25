using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Text that acts like a hyperlink: drawn in the accent colour, underlined-looking when focused, and
/// activated with Enter, Space or a click. It raises <see cref="Activated"/>; what that does (open a URL,
/// show help, switch a tab) is up to the host. Unlike a <see cref="Buttons.Button"/> it carries no frame, so
/// it fits inside a sentence-sized row.
/// </summary>
public class LinkLabel : Control
{
    private string _text = string.Empty;

    public LinkLabel(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(0, 1);
    }

    /// <summary>The visible text; the width follows it.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            ViewSize = new Size(_text.Length, 1);
            StateHasChanged();
        }
    }

    /// <summary>What the link points at, for the host's handler (not drawn).</summary>
    public string Target { get; set; } = string.Empty;

    public event EventHandler? Activated;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled || _text.Length == 0)
        {
            yield break;
        }

        yield return ("Enter/Space", "Follow the link");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        if (keyEvent.ConsoleKey is ConsoleKey.Enter or ConsoleKey.Spacebar)
        {
            Activated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (IsEnabled && mouseEvent.LeftClicked && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            if (!IsFocused)
            {
                SetFocus();
            }

            Activated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return isHandled;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (_text.Length == 0)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        Color foreground = !IsEnabled ? colorScheme.DisabledForeground : IsFocused ? colorScheme.SelectedFocusedForeground : Color.Cyan;
        Color background = IsFocused && IsEnabled ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
        // The focused link is underlined as well as recoloured, so the focus shows even where the colours do not stand out.
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, new ConxelString(_text, foreground, background, isUnderline: IsFocused && IsEnabled));
    }
}
