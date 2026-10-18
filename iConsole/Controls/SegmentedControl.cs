using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A one-line pick-one control: the options side by side (<c> Day  Week  Month </c>) with the chosen one
/// highlighted. Left/Right (and Home/End) change the choice, a click on an option picks it, the wheel steps.
/// Unlike a <see cref="RadioGroup"/> it takes one row and never needs a list to scroll.
/// </summary>
public class SegmentedControl : Control, IReadOnly
{
    private List<string> _options = [];
    private int _selectedIndex;

    public SegmentedControl(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(20, 1);
    }

    public bool IsReadOnly { get; set; }

    public event EventHandler? SelectionChanged;

    public IReadOnlyList<string> Options
    {
        get => _options;
        set
        {
            _options = [.. value ?? []];
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _options.Count - 1));
            StateHasChanged();
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, Math.Max(0, _options.Count - 1));
            if (_selectedIndex != clamped)
            {
                _selectedIndex = clamped;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    public string? SelectedOption => _selectedIndex < _options.Count ? _options[_selectedIndex] : null;

    /// <summary>Text of option <paramref name="index"/> as drawn: one cell of padding either side.</summary>
    private string Segment(int index) => " " + _options[index] + " ";

    /// <summary>Index of the option under <paramref name="column"/> (relative to the control), or -1.</summary>
    public int IndexAtColumn(int column)
    {
        int start = 0;
        for (int i = 0; i < _options.Count; i++)
        {
            int end = start + Segment(i).Length;
            if (column >= start && column < end)
            {
                return i;
            }

            start = end + 1;
        }

        return -1;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || IsReadOnly || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.RightArrow:
                SelectedIndex++;
                return true;
            case ConsoleKey.LeftArrow:
                SelectedIndex--;
                return true;
            case ConsoleKey.Home:
                SelectedIndex = 0;
                return true;
            case ConsoleKey.End:
                SelectedIndex = _options.Count - 1;
                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (IsReadOnly || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            SelectedIndex += mouseEvent.WheelDelta > 0 ? -1 : 1;
            return true;
        }

        if (mouseEvent.LeftClicked)
        {
            int index = IndexAtColumn((int)mouseEvent.Position.X - (int)AbsolutePosition.X);
            if (index >= 0)
            {
                if (!IsFocused)
                {
                    SetFocus();
                }

                SelectedIndex = index;
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
        int width = (int)ViewSize.Width;
        ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
        bool disabled = IsReadOnly || !IsEnabled;
        for (int i = 0; i < _options.Count && line.Count < width; i++)
        {
            Color foreground;
            Color background;
            if (i == _selectedIndex)
            {
                foreground = disabled ? colorScheme.DisabledSelectedForeground : IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.SelectedForeground;
                background = disabled ? colorScheme.DisabledBackground : IsFocused ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground;
            }
            else
            {
                foreground = disabled ? colorScheme.DisabledForeground : colorScheme.Foreground;
                background = disabled ? colorScheme.DisabledBackground : colorScheme.Background;
            }

            line.Append(Segment(i), foreground, background);
            line.Append(" ", colorScheme.Foreground, colorScheme.Background);
        }

        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
