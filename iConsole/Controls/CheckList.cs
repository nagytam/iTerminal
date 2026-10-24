using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A scrolling list of checkable items (<c>[x] Music</c>) for picking any number of options. Up/Down/PageUp/
/// PageDown/Home/End move the cursor, Space toggles the cursor row, <c>*</c> inverts every row, Enter raises
/// <see cref="Activated"/>. A click on a row moves the cursor there and toggles it; the wheel moves it three
/// rows. Every row is painted, the ones past the last item blank (or <see cref="EmptyText"/>).
/// </summary>
public class CheckList : Control, IReadOnly
{
    private const int WheelRows = 3;

    private readonly List<(string Text, bool Checked)> _items = [];
    private int _cursor;
    private int _scroll;

    public CheckList(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(30, 5);
    }

    public bool IsReadOnly { get; set; }

    /// <summary>Drawn on the first row, greyed, while there are no items.</summary>
    public string EmptyText { get; set; } = string.Empty;

    public IReadOnlyList<(string Text, bool Checked)> Items => _items;

    public int CursorIndex => _cursor;

    public IEnumerable<string> CheckedItems => _items.Where(i => i.Checked).Select(i => i.Text);

    /// <summary>Raised after any check state changes.</summary>
    public event EventHandler? CheckedChanged;

    /// <summary>Raised when the user presses Enter on a row; the argument is the row's index.</summary>
    public event EventHandler<int>? Activated;

    public void SetItems(IEnumerable<string> texts, IEnumerable<string>? checkedTexts = null)
    {
        HashSet<string> preChecked = [.. checkedTexts ?? []];
        _items.Clear();
        _items.AddRange(texts.Select(t => (t, preChecked.Contains(t))));
        _scroll = 0;
        // Scrolls back to the cursor: it is kept, so a window at the top would leave it off screen.
        MoveCursor(_cursor);
    }

    public void SetChecked(int index, bool isChecked)
    {
        if (index >= 0 && index < _items.Count && _items[index].Checked != isChecked)
        {
            _items[index] = (_items[index].Text, isChecked);
            CheckedChanged?.Invoke(this, EventArgs.Empty);
            StateHasChanged();
        }
    }

    /// <summary>Checks or unchecks every item.</summary>
    public void SetAll(bool isChecked)
    {
        if (_items.Count == 0 || _items.All(item => item.Checked == isChecked))
        {
            return;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            _items[i] = (_items[i].Text, isChecked);
        }

        CheckedChanged?.Invoke(this, EventArgs.Empty);
        StateHasChanged();
    }

    public void InvertAll()
    {
        if (_items.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            _items[i] = (_items[i].Text, !_items[i].Checked);
        }

        CheckedChanged?.Invoke(this, EventArgs.Empty);
        StateHasChanged();
    }

    private void MoveCursor(int index)
    {
        _cursor = Math.Clamp(index, 0, Math.Max(0, _items.Count - 1));
        int height = Math.Max(1, (int)ViewSize.Height);
        if (_cursor < _scroll)
        {
            _scroll = _cursor;
        }
        else if (_cursor >= _scroll + height)
        {
            _scroll = _cursor - height + 1;
        }

        StateHasChanged();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled || _items.Count == 0)
        {
            yield break;
        }

        yield return ("Up/Down", "Move the cursor");
        yield return ("PageUp/PageDown", "Move the cursor a page");
        yield return ("Home/End", "First or last item");
        yield return ("Enter", "Activate the list");
        if (!IsReadOnly)
        {
            yield return ("Space", "Check or uncheck the item under the cursor");
            yield return ("+/-", "Check or uncheck every item");
            yield return ("*", "Invert every item");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled || _items.Count == 0)
        {
            return isHandled;
        }

        int page = Math.Max(1, (int)ViewSize.Height - 1);
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.DownArrow:
                MoveCursor(_cursor + 1);
                return true;
            case ConsoleKey.UpArrow:
                MoveCursor(_cursor - 1);
                return true;
            case ConsoleKey.PageDown:
                MoveCursor(_cursor + page);
                return true;
            case ConsoleKey.PageUp:
                MoveCursor(_cursor - page);
                return true;
            case ConsoleKey.Home:
                MoveCursor(0);
                return true;
            case ConsoleKey.End:
                MoveCursor(_items.Count - 1);
                return true;
            case ConsoleKey.Spacebar:
                if (!IsReadOnly)
                {
                    SetChecked(_cursor, !_items[_cursor].Checked);
                }

                return true;
            case ConsoleKey.Enter:
                Activated?.Invoke(this, _cursor);
                return true;
        }

        if (keyEvent.KeyChar == '*' && !IsReadOnly)
        {
            InvertAll();
            return true;
        }

        if (keyEvent.KeyChar is '+' or '-' && !IsReadOnly)
        {
            SetAll(keyEvent.KeyChar == '+');
            return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (_items.Count == 0 || !IsEnabled || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            MoveCursor(_cursor + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows));
            return true;
        }

        if (mouseEvent.LeftClicked)
        {
            int index = _scroll + (int)mouseEvent.Position.Y - (int)AbsolutePosition.Y;
            if (index < _items.Count)
            {
                if (!IsFocused)
                {
                    SetFocus();
                }

                MoveCursor(index);
                if (!IsReadOnly)
                {
                    SetChecked(index, !_items[index].Checked);
                }

                return true;
            }
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
        int width = (int)ViewSize.Width;
        for (int y = 0; y < (int)ViewSize.Height; y++)
        {
            int index = _scroll + y;
            string text;
            Color foreground = colorScheme.Foreground;
            Color background = colorScheme.Background;
            if (index < _items.Count)
            {
                text = (_items[index].Checked ? "[x] " : "[ ] ") + _items[index].Text;
                if (index == _cursor)
                {
                    foreground = IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.SelectedForeground;
                    background = IsFocused ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground;
                }
                else if (IsReadOnly || !IsEnabled)
                {
                    foreground = colorScheme.DisabledForeground;
                }
            }
            else
            {
                text = _items.Count == 0 && y == 0 ? EmptyText : string.Empty;
                foreground = text.Length > 0 ? colorScheme.DisabledForeground : colorScheme.Foreground;
            }

            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)y), new Size(width, 1),
                new ConxelString(text.Cut(width).PadRight(width), foreground, background));
        }
    }
}
