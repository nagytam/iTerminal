using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A one-line list of removable tags (<c>[rock ×] [jazz ×] [+]</c>) for multi-value fields such as genres or
/// filters. Left/Right move between chips, Delete or Backspace removes the highlighted one, Enter on the
/// trailing <c>[+]</c> (or a click on it) raises <see cref="AddRequested"/> so the host can ask for a value.
/// A click on a chip highlights it; a click on its × removes it. Chips that do not fit are dropped from the
/// left of the visible window, so the highlighted one is always on screen.
/// </summary>
public class Chips : Control, IReadOnly
{
    private readonly List<string> _items = [];
    private int _active;

    public Chips(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(40, 1);
    }

    private bool _isReadOnly;

    /// <summary>Hides the <c>[+]</c> chip and the ×; the highlight leaves the <c>[+]</c> slot when it goes.</summary>
    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            ActiveIndex = _active;
        }
    }

    public IReadOnlyList<string> Items => _items;

    public event EventHandler? ItemsChanged;

    /// <summary>Raised when the user activates the <c>[+]</c> chip.</summary>
    public event EventHandler? AddRequested;

    /// <summary>The highlighted chip; <c>Items.Count</c> is the <c>[+]</c> chip.</summary>
    public int ActiveIndex
    {
        get => _active;
        set
        {
            _active = Math.Clamp(value, 0, IsReadOnly ? Math.Max(0, _items.Count - 1) : _items.Count);
            StateHasChanged();
        }
    }

    public bool Add(string item)
    {
        string trimmed = item?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || _items.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        _items.Add(trimmed);
        ItemsChanged?.Invoke(this, EventArgs.Empty);
        StateHasChanged();
        return true;
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return false;
        }

        _items.RemoveAt(index);
        ActiveIndex = _active;
        ItemsChanged?.Invoke(this, EventArgs.Empty);
        StateHasChanged();
        return true;
    }

    private string Label(int index) => index < _items.Count
        ? (IsReadOnly ? $"[{_items[index]}]" : $"[{_items[index]} ×]")
        : "[+]";

    private int ChipCount => IsReadOnly ? _items.Count : _items.Count + 1;

    /// <summary>
    /// The chips shown for <paramref name="width"/> cells as (index, start column) pairs: the window starts at
    /// the first chip, and slides right until the highlighted one fits.
    /// </summary>
    public List<(int Index, int Start)> Visible(int width)
    {
        for (int first = 0; first <= _active && first < ChipCount; first++)
        {
            List<(int, int)> shown = [];
            int column = 0;
            for (int i = first; i < ChipCount; i++)
            {
                int length = Label(i).Length;
                if (column + length > width)
                {
                    break;
                }

                shown.Add((i, column));
                column += length + 1;
            }

            if (shown.Any(s => s.Item1 == _active) || first == _active)
            {
                // A chip wider than the control still shows, cut at the edge, instead of leaving the row blank.
                return shown.Count == 0 ? [(_active, 0)] : shown;
            }
        }

        return [];
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Left/Right", "Move between the chips");
        yield return ("Home/End", "First chip or last chip");
        if (!IsReadOnly)
        {
            yield return ("Enter", "Add a chip on the [+] slot");
            yield return ("Delete/Backspace", "Remove the active chip");
        }
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
            case ConsoleKey.LeftArrow:
                ActiveIndex--;
                return true;
            case ConsoleKey.RightArrow:
                ActiveIndex++;
                return true;
            case ConsoleKey.Home:
                ActiveIndex = 0;
                return true;
            case ConsoleKey.End:
                ActiveIndex = int.MaxValue;
                return true;
            case ConsoleKey.Delete:
            case ConsoleKey.Backspace:
                // Keys that do nothing here are left to the host: Enter on a chip is the dialog's default button.
                return !IsReadOnly && RemoveAt(_active);
            case ConsoleKey.Enter:
                if (!IsReadOnly && _active == _items.Count)
                {
                    AddRequested?.Invoke(this, EventArgs.Empty);
                    return true;
                }

                return false;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (IsEnabled && mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            ActiveIndex += mouseEvent.WheelDelta > 0 ? -1 : 1;
            return true;
        }

        if (!IsEnabled || !mouseEvent.LeftClicked || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        int column = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
        if (!IsFocused)
        {
            SetFocus();
        }

        foreach ((int index, int start) in Visible((int)ViewSize.Width))
        {
            int length = Label(index).Length;
            if (column < start || column >= start + length)
            {
                continue;
            }

            if (index == _items.Count)
            {
                ActiveIndex = index;
                if (!IsReadOnly)
                {
                    AddRequested?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (!IsReadOnly && column == start + length - 2)
            {
                RemoveAt(index);
            }
            else
            {
                ActiveIndex = index;
            }

            return true;
        }

        return true;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
        int column = 0;
        foreach ((int index, int start) in Visible(width))
        {
            if (start > column)
            {
                line.Append(new string(' ', start - column), colorScheme.Foreground, colorScheme.Background);
            }

            string label = Label(index);
            bool active = index == _active && IsFocused;
            Color foreground = !IsEnabled ? colorScheme.DisabledForeground
                : active ? colorScheme.SelectedFocusedForeground
                : index == _items.Count ? colorScheme.DisabledForeground : colorScheme.Foreground;
            Color background = active ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
            line.Append(label, foreground, background);
            column = start + label.Length;
        }

        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
