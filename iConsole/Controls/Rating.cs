using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A star rating (<c>★★★☆☆</c>): Left/Right (or a digit key) change the value, a click on a star sets it,
/// and clicking the current value again clears it to 0. <see cref="Maximum"/> is the number of stars.
/// </summary>
public class Rating : Control, IReadOnly
{
    private int _value;
    private int _maximum = 5;

    public Rating(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(5, 1);
    }

    public bool IsReadOnly { get; set; }

    public event EventHandler? ValueChanged;

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(1, value);
            ViewSize = new Size(_maximum, 1);
            Value = _value;
            StateHasChanged();
        }
    }

    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, 0, _maximum);
            if (_value != clamped)
            {
                _value = clamped;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (IsReadOnly || !IsEnabled)
        {
            yield break;
        }

        yield return ("Left/Right", "Lower or raise the rating");
        yield return ("0-9", "Set the rating directly");
        yield return ("Home/End", "Clear the rating or give the maximum");
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
                Value++;
                return true;
            case ConsoleKey.LeftArrow:
                Value--;
                return true;
            case ConsoleKey.Home:
                Value = 0;
                return true;
            case ConsoleKey.End:
                Value = _maximum;
                return true;
        }

        if (keyEvent.KeyChar is >= '0' and <= '9')
        {
            Value = keyEvent.KeyChar - '0';
            return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (!IsReadOnly && IsEnabled && mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            // Up for more, as on the numeric box.
            Value += mouseEvent.WheelDelta > 0 ? 1 : -1;
            return true;
        }

        if (IsReadOnly || !IsEnabled || !mouseEvent.LeftClicked || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        int star = (int)mouseEvent.Position.X - (int)AbsolutePosition.X + 1;
        if (!IsFocused)
        {
            SetFocus();
        }

        Value = star == _value ? 0 : star;
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
        bool disabled = IsReadOnly || !IsEnabled;
        Color filled = disabled ? colorScheme.DisabledForeground : Color.Yellow;
        Color empty = disabled ? colorScheme.DisabledForeground : IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
        ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
        for (int i = 0; i < _maximum; i++)
        {
            line.Append(i < _value ? "★" : "☆", i < _value ? filled : empty, colorScheme.Background);
        }

        line.Expand((int)ViewSize.Width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
