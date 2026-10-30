using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A collapsible section: a header row (<c>▶ Advanced</c> / <c>▼ Advanced</c>) and, when expanded, a
/// <see cref="Content"/> control on the rows below. Space, Enter, Left/Right or a click on the header
/// toggle it. The content is attached and detached with the state, so a collapsed section takes one
/// row and its children neither draw nor take the keyboard; <see cref="Control.ViewSize"/> follows.
/// </summary>
public class Expander : Control
{
    private bool _isExpanded;
    private IControl? _content;

    public Expander(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(30, 1);
    }

    private string _header = string.Empty;
    private int _contentHeight = 3;

    public string Header
    {
        get => _header;
        set
        {
            _header = value ?? string.Empty;
            StateHasChanged();
        }
    }

    /// <summary>Rows the expanded content takes (the header is one more). Changing it resizes an open section.</summary>
    public int ContentHeight
    {
        get => _contentHeight;
        set
        {
            _contentHeight = Math.Max(0, value);
            if (_isExpanded && _content is not null)
            {
                _content.ViewSize = new Size(ViewSize.Width, _contentHeight);
            }

            ViewSize = new Size(ViewSize.Width, _isExpanded && _content is not null ? 1 + _contentHeight : 1);
            StateHasChanged();
        }
    }

    public event EventHandler<bool>? ExpandedChanged;

    public IControl? Content
    {
        get => _content;
        set
        {
            Detach();
            _content = value;
            ApplyState();
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                ApplyState();
                ExpandedChanged?.Invoke(this, _isExpanded);
            }
        }
    }

    private void Detach()
    {
        if (_content is not null && Controls.Contains(_content))
        {
            RemoveControl(_content);
        }
    }

    private void ApplyState()
    {
        ViewSize = new Size(ViewSize.Width, _isExpanded && _content is not null ? 1 + ContentHeight : 1);
        if (_content is not null)
        {
            if (_isExpanded && !Controls.Contains(_content))
            {
                _content.Position = new Position(0, 1);
                _content.ViewSize = new Size(ViewSize.Width, ContentHeight);
                AddControl(_content);
            }
            else if (!_isExpanded)
            {
                Detach();
            }
        }

        StateHasChanged();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Enter/Space", UiText.Get("Hint.ExpandOrCollapseSection", "Expand or collapse the section"));
        yield return ("Right", UiText.Get("Hint.ExpandSection", "Expand the section"));
        yield return ("Left", UiText.Get("Hint.CollapseSection", "Collapse the section"));
        yield return ("+/-", UiText.Get("Hint.ExpandOrCollapseSection", "Expand or collapse the section"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Spacebar:
            case ConsoleKey.Enter:
                IsExpanded = !IsExpanded;
                return true;
            case ConsoleKey.RightArrow:
                IsExpanded = true;
                return true;
            case ConsoleKey.LeftArrow:
                IsExpanded = false;
                return true;
        }

        if (keyEvent.KeyChar is '+' or '-')
        {
            IsExpanded = keyEvent.KeyChar == '+';
            return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (isHandled)
        {
            return true;
        }

        if (mouseEvent.LeftClicked
            && IsEnabled
            && mouseEvent.Position.Y == AbsolutePosition.Y
            && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            if (!IsFocused)
            {
                SetFocus();
            }

            IsExpanded = !IsExpanded;
            return true;
        }

        return false;
    }

    public override void Draw(IBuffer buffer)
    {
        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        Color foreground = IsEnabled ? IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground : colorScheme.DisabledForeground;
        Color background = IsEnabled && IsFocused ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
        string header = ((_isExpanded ? "▼ " : "▶ ") + Header).PadRight(width);
        buffer.Write(ZOrder, AbsolutePosition, new Size(ViewSize.Width, 1), new ConxelString(header[..width], foreground, background));
        base.Draw(buffer);
    }
}
