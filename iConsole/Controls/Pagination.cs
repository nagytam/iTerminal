using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A one-line pager (<c>‹ Page 3 of 12 ›</c>) for results that arrive in pages. Left/Right (or PageUp/
/// PageDown) change the page, Home/End jump to the first and last, a click on an arrow steps and the wheel
/// steps. <see cref="PageCount"/> is derived from <see cref="TotalItems"/> and <see cref="PageSize"/>.
/// </summary>
public class Pagination : Control
{
    private int _totalItems;
    private int _pageSize = 10;
    private int _page;

    public Pagination(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(20, 1);
    }

    public event EventHandler? PageChanged;

    public int TotalItems
    {
        get => _totalItems;
        set
        {
            _totalItems = Math.Max(0, value);
            Page = _page;
            StateHasChanged();
        }
    }

    public int PageSize
    {
        get => _pageSize;
        set
        {
            _pageSize = Math.Max(1, value);
            Page = _page;
            StateHasChanged();
        }
    }

    /// <summary>Number of pages; an empty list still has one (empty) page.</summary>
    public int PageCount => Math.Max(1, (_totalItems + _pageSize - 1) / _pageSize);

    /// <summary>Zero-based current page, clamped to [0, <see cref="PageCount"/> - 1].</summary>
    public int Page
    {
        get => _page;
        set
        {
            int clamped = Math.Clamp(value, 0, PageCount - 1);
            if (_page != clamped)
            {
                _page = clamped;
                PageChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    /// <summary>Index of the first item on the current page.</summary>
    public int FirstItemIndex => _page * _pageSize;

    /// <summary>The text between the arrows.</summary>
    public string Caption => $"Page {_page + 1} of {PageCount}";

    /// <summary>
    /// The caption as drawn: the full <see cref="Caption"/> if it fits beside the two arrows, else <c>3/12</c>, else
    /// as much of that as fits. A narrow pager keeps its right arrow on the row this way, where the caption used to push it off.
    /// </summary>
    private string FittedCaption
    {
        get
        {
            // Four cells go to the arrows and the spaces that surround the caption.
            int room = Math.Max(0, (int)ViewSize.Width - 4);
            if (Caption.Length <= room)
            {
                return Caption;
            }

            string compact = $"{_page + 1}/{PageCount}";
            return compact.Length <= room ? compact : compact[..room];
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Left/Right", UiText.Get("Hint.PreviousNextPage", "Previous or next page"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.PreviousNextPage", "Previous or next page"));
        yield return ("Home/End", UiText.Get("Hint.FirstOrLastPage", "First or last page"));
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
            case ConsoleKey.RightArrow:
            case ConsoleKey.PageDown:
                Page++;
                return true;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.PageUp:
                Page--;
                return true;
            case ConsoleKey.Home:
                Page = 0;
                return true;
            case ConsoleKey.End:
                Page = PageCount - 1;
                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (!IsEnabled || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            Page += mouseEvent.WheelDelta > 0 ? -1 : 1;
            return true;
        }

        if (mouseEvent.LeftClicked)
        {
            int column = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
            // Drawn as ‹ + " " + FittedCaption + " " + ›: the right arrow sits after the caption and both spaces.
            int right = FittedCaption.Length + 3;
            if (column == 0)
            {
                Page--;
            }
            else if (column == right)
            {
                Page++;
            }

            if (!IsFocused)
            {
                SetFocus();
            }

            return true;
        }

        return isHandled;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        Color active = !IsEnabled ? colorScheme.DisabledForeground : IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
        Color text = IsEnabled ? colorScheme.Foreground : colorScheme.DisabledForeground;
        Color dim = colorScheme.DisabledForeground;
        ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
        line.Append("‹", _page > 0 ? active : dim, colorScheme.Background);
        line.Append(" " + FittedCaption + " ", text, colorScheme.Background);
        line.Append("›", _page < PageCount - 1 ? active : dim, colorScheme.Background);
        line.Expand((int)ViewSize.Width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
