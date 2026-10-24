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
            int clamped = Math.Clamp(_selectedIndex, 0, Math.Max(0, _options.Count - 1));
            bool moved = clamped != _selectedIndex;
            _selectedIndex = clamped;
            StateHasChanged();
            if (moved)
            {
                // A shorter list moved the choice: whoever listens to it must hear about that.
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
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

    /// <summary>
    /// The options shown for <paramref name="width"/> cells as (index, start column) pairs. The window starts at
    /// the first option and slides right until the chosen one fits, so a choice past the edge stays visible.
    /// </summary>
    public List<(int Index, int Start)> VisibleSegments(int width)
    {
        for (int first = 0; first <= _selectedIndex && first < _options.Count; first++)
        {
            List<(int Index, int Start)> shown = [];
            int column = 0;
            for (int i = first; i < _options.Count; i++)
            {
                int length = Segment(i).Length;
                if (column + length > width)
                {
                    break;
                }

                shown.Add((i, column));
                column += length + 1;
            }

            if (shown.Any(s => s.Index == _selectedIndex) || first == _selectedIndex)
            {
                // An option wider than the control still shows, cut at the edge, instead of leaving the row blank.
                return shown.Count == 0 ? [(_selectedIndex, 0)] : shown;
            }
        }

        return [];
    }

    /// <summary>Index of the visible option under <paramref name="column"/> (relative to the control), or -1.</summary>
    public int IndexAtColumn(int column)
    {
        foreach ((int index, int start) in VisibleSegments((int)ViewSize.Width))
        {
            if (column >= start && column < start + Segment(index).Length)
            {
                return index;
            }
        }

        return -1;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (IsReadOnly || !IsEnabled)
        {
            yield break;
        }

        yield return ("Left/Right", "Choose the previous or next segment");
        yield return ("Up/Down", "Choose the previous or next segment");
        yield return ("Home/End", "Choose the first or last segment");
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
            case ConsoleKey.DownArrow:
                SelectedIndex++;
                return true;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.UpArrow:
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
        if (IsReadOnly || !IsEnabled || !AbsoluteBoundRect.Contains(mouseEvent.Position))
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
        int column = 0;
        foreach ((int i, int start) in VisibleSegments(width))
        {
            if (start > column)
            {
                line.Append(new string(' ', start - column), colorScheme.Foreground, colorScheme.Background);
            }

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
            column = start + Segment(i).Length;
        }

        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
