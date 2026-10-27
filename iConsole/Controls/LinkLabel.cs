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
        PointerShape = PointerShape.Pointer;
        ViewSize = new Size(0, 1);
    }

    /// <summary>The visible text; the width follows it.</summary>
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            Size size = new(_text.Length, 1);
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

    /// <summary>
    /// What the link points at, for the host's handler. A web, file, FTP, SSH or mail address (<c>https://…</c>, <c>file://…</c>) is also written
    /// as an OSC 8 hyperlink, so a terminal that supports them opens it on a Ctrl+click; anything else is only for the handler.
    /// </summary>
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
        int width = (int)ViewSize.Width;
        if (width <= 0)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        if (_text.Length == 0)
        {
            // Painted blank, not skipped: a link whose text was cleared used to leave the old text behind.
            ConxelString blank = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
            blank.Expand(width, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, AbsolutePosition, ViewSize, blank);
            return;
        }

        Color foreground = !IsEnabled ? colorScheme.DisabledForeground : IsFocused ? colorScheme.SelectedFocusedForeground : Color.Cyan;
        Color background = IsFocused && IsEnabled ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
        // The focused link is underlined as well as recoloured, so the focus shows even where the colours do not stand out.
        ConxelString line = new(_text, foreground, background, isUnderline: IsFocused && IsEnabled);
        if (IsEnabled && Uri.TryCreate(Target, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https" or "ftp" or "ftps" or "sftp" or "ssh" or "file" or "mailto")
        {
            line.SetHyperlink(uri.OriginalString);
        }

        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
