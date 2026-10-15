using iConsole.Buffers;
using iConsole.Data;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A mutually-exclusive set of <see cref="RadioButton"/> options. The group is the single focusable
/// unit (its options are not individual tab stops); Up/Down (or Left/Right) move the selection,
/// Home/End jump to the ends, and clicking an option selects it. Selecting one option clears the
/// rest. Styling and read-only behaviour follow the other interactive controls.
/// </summary>
public class RadioGroup<T> : Control, IReadOnly
{
    private readonly List<T> _items = [];
    private readonly List<RadioButton> _buttons = [];
    private int _selectedIndex = -1;
    private bool _isReadOnly;

    public RadioGroup(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(20, 1);
    }

    public Orientation Orientation { get; set; } = Orientation.Vertical;
    public Func<T, string> DisplaySelector { get; set; } = item => item?.ToString() ?? string.Empty;

    public event EventHandler? SelectionChanged;

    public IReadOnlyList<T> Items => _items;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        set
        {
            _isReadOnly = value;
            foreach (var button in _buttons)
            {
                button.IsReadOnly = value;
            }
            StateHasChanged();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (_selectedIndex != clamped)
            {
                _selectedIndex = clamped;
                ApplySelection();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    public T? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : default;

    public void SetItems(IEnumerable<T> items)
    {
        foreach (var button in _buttons)
        {
            RemoveControl(button);
        }
        _buttons.Clear();
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count == 0 ? -1 : 0;

        for (var i = 0; i < _items.Count; i++)
        {
            var index = i;
            var button = new RadioButton(ThemeService)
            {
                Label = DisplaySelector(_items[i]),
                CanFocus = false,
                IsReadOnly = _isReadOnly,
                IsSelected = i == _selectedIndex,
            };
            button.SelectRequested += (_, _) =>
            {
                SelectedIndex = index;
                SetFocus();
            };
            _buttons.Add(button);
            AddControl(button);
        }

        LayoutButtons();
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutButtons();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // Only with focus: a key the focused control declines is offered to every control.
        if (!IsReadOnly && IsFocused && _items.Count > 0)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.LeftArrow:
                    Move(-1);
                    return true;
                case ConsoleKey.DownArrow:
                case ConsoleKey.RightArrow:
                    Move(1);
                    return true;
                case ConsoleKey.Home:
                    SelectedIndex = 0;
                    return true;
                case ConsoleKey.End:
                    SelectedIndex = _items.Count - 1;
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        // The group hit-tests its own rows. Adjacent single-row child controls overlap by one cell
        // at their shared boundary (Rectangle.Contains is inclusive on both edges), so delegating
        // clicks to the children would let the upper option steal a click meant for the lower one.
        if (!IsReadOnly && mouseEvent.WheelDelta == 0 && (mouseEvent.LeftClicked || mouseEvent.RightClicked))
        {
            var index = HitTest(
                (int)mouseEvent.Position.X - (int)AbsolutePosition.X,
                (int)mouseEvent.Position.Y - (int)AbsolutePosition.Y);
            if (index >= 0)
            {
                SelectedIndex = index;
                SetFocus();
            }
            return index >= 0;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        // The selected option carries the focused look only while the group itself holds focus.
        for (var i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].IsHighlighted = IsFocused && i == _selectedIndex;
        }

        base.Draw(buffer);
    }

    private void Move(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Clamp((_selectedIndex < 0 ? 0 : _selectedIndex) + delta, 0, _items.Count - 1);
    }

    private int HitTest(int relativeX, int relativeY)
    {
        if (relativeX < 0 || _items.Count == 0)
        {
            return -1;
        }

        if (Orientation == Orientation.Vertical)
        {
            return relativeY >= 0 && relativeY < _items.Count ? relativeY : -1;
        }

        if (relativeY != 0)
        {
            return -1;
        }

        var x = 0;
        for (var i = 0; i < _buttons.Count; i++)
        {
            var width = 2 + _buttons[i].Label.Length;
            if (relativeX >= x && relativeX < x + width)
            {
                return i;
            }
            x += width + 2;
        }

        return -1;
    }

    private void ApplySelection()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].IsSelected = i == _selectedIndex;
        }
    }

    private void LayoutButtons()
    {
        if (Orientation == Orientation.Vertical)
        {
            var width = Math.Max(1, (int)ViewSize.Width);
            for (var i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].Position = new Position(0, i);
                _buttons[i].ViewSize = new Size(width, 1);
            }
        }
        else
        {
            var x = 0;
            foreach (var button in _buttons)
            {
                // "◉ " glyph (2 cells) + label, with a two-cell gap between options.
                var width = 2 + button.Label.Length;
                button.Position = new Position(x, 0);
                button.ViewSize = new Size(width, 1);
                x += width + 2;
            }
        }
    }
}
