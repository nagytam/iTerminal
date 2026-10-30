using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A single-line integer input with up/down spinner affordances. The value is always kept inside
/// [<see cref="Minimum"/>, <see cref="Maximum"/>]. The numeric "well" is styled like
/// <see cref="TextBox"/> so text and numeric inputs read as one family, and the focus / disabled /
/// read-only behaviour matches the other interactive controls (<see cref="CheckBox"/>,
/// <see cref="Button"/>).
///
/// <para>Keys: Up/Down step by <see cref="Step"/>, PageUp/PageDown by <see cref="LargeStep"/>,
/// Home/End jump to the bounds, digits type a value, Backspace drops the last digit and
/// <c>-</c> flips the sign when the range allows negatives. The mouse wheel steps the value and
/// clicking the ↑/↓ glyphs increments/decrements.</para>
/// </summary>
public class NumericUpDown : Control, IReadOnly
{
    private long _value;
    private long _minimum;
    private long _maximum = 100;

    // Buffer offsets of the spinner glyphs, recomputed each Draw for mouse hit-testing.
    private int _upArrowOffset = -1;
    private int _downArrowOffset = -1;

    // "-" on a zero cannot flip anything, so it is remembered for the next digit: typing -5 gives -5, not 5.
    private bool _pendingMinus;

    public NumericUpDown(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(16, 1);
    }

    private string _label = string.Empty;

    public string Label
    {
        get => _label;
        set
        {
            _label = value ?? string.Empty;
            StateHasChanged();
        }
    }

    public bool IsReadOnly { get; set; }
    private long _step = 1;
    private long _largeStep = 10;

    /// <summary>Distance of an arrow key or a wheel notch; at least 1, since a zero or negative step would stall or reverse the keys.</summary>
    public long Step
    {
        get => _step;
        set => _step = Math.Max(1, value);
    }

    /// <summary>Distance of PageUp/PageDown; at least 1.</summary>
    public long LargeStep
    {
        get => _largeStep;
        set => _largeStep = Math.Max(1, value);
    }

    public event EventHandler? ValueChanged;

    public long Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum)
            {
                _maximum = _minimum;
            }
            Value = _value; // re-clamp into the new range
        }
    }

    public long Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            if (_minimum > _maximum)
            {
                _minimum = _maximum;
            }
            Value = _value; // re-clamp into the new range
        }
    }

    public long Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, _minimum, _maximum);
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

        yield return ("Up/Down", UiText.Get("Hint.StepValue", "Step the value"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.StepValueLarge", "Step the value by a large amount"));
        yield return ("Home/End", UiText.Get("Hint.SetMinMax", "Set the minimum or maximum"));
        yield return ("0-9", UiText.Get("Hint.TypeValue", "Type the value"));
        if (_minimum < 0)
        {
            yield return ("-", UiText.Get("Hint.FlipSign", "Flip the sign"));
        }

        yield return ("Backspace", UiText.Get("Hint.RemoveLastDigit", "Remove the last digit"));
        yield return ("Delete", UiText.Get("Hint.SetValueZero", "Set the value to 0"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);

        // Only with focus: a key the focused control declines is offered to every control, and an
        // unfocused spinner would take the arrows that move the cursor in a list beside it.
        if (isHandled || IsReadOnly || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        bool minusPending = _pendingMinus;
        _pendingMinus = false;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                Increment(Step);
                return true;
            case ConsoleKey.DownArrow:
                Increment(-Step);
                return true;
            case ConsoleKey.PageUp:
                Increment(LargeStep);
                return true;
            case ConsoleKey.PageDown:
                Increment(-LargeStep);
                return true;
            case ConsoleKey.Home:
                Value = Minimum;
                return true;
            case ConsoleKey.End:
                Value = Maximum;
                return true;
            case ConsoleKey.Backspace:
                Value = _value / 10;
                return true;
            case ConsoleKey.Delete:
                Value = 0;
                return true;
        }

        if (!keyEvent.Control && !keyEvent.Alt)
        {
            if (char.IsDigit(keyEvent.KeyChar))
            {
                AppendDigit(keyEvent.KeyChar - '0', minusPending);
                return true;
            }
            if (keyEvent.KeyChar == '-' && _minimum < 0)
            {
                _pendingMinus = _value == 0 && !minusPending;
                Value = SafeNegate(_value);
                return true;
            }
            if (keyEvent.KeyChar == '+' && _value < 0)
            {
                Value = SafeNegate(_value);
                return true;
            }
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (IsReadOnly || !IsEnabled || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        // A remembered "-" belongs to the keyboard entry it started: a click or a wheel notch ends that entry.
        if (mouseEvent.WheelDelta != 0 || mouseEvent.LeftClicked)
        {
            _pendingMinus = false;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            Increment(mouseEvent.WheelDelta > 0 ? Step : -Step);
            return true;
        }

        // Only the left button acts, as on Button and CheckBox; a click anywhere on the control takes the focus,
        // so the value box can be focused by a click too, not only the arrows.
        if (mouseEvent.LeftClicked)
        {
            if (!IsFocused)
            {
                SetFocus();
            }

            var x = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
            if (x == _upArrowOffset)
            {
                Increment(Step);
                return true;
            }
            if (x == _downArrowOffset)
            {
                Increment(-Step);
                return true;
            }
            return true;
        }

        return isHandled;
    }

    private void Increment(long delta) => Value = SafeAdd(_value, delta);

    private void AppendDigit(int digit, bool minusPending)
    {
        var negative = _value < 0 || (minusPending && _value == 0);
        var magnitude = _value == long.MinValue ? long.MaxValue : Math.Abs(_value);
        var candidate = SafeAdd(SafeMultiplyByTen(magnitude), digit);
        var typed = negative ? SafeNegate(candidate) : candidate;
        // A digit that would carry the value past the bound starts a new entry instead of sticking to the bound.
        if (typed > _maximum || typed < _minimum)
        {
            var fresh = negative ? -(long)digit : digit;
            if (fresh >= _minimum && fresh <= _maximum)
            {
                typed = fresh;
            }
        }

        Value = typed;
    }

    private static long SafeAdd(long a, long b)
    {
        try
        {
            return checked(a + b);
        }
        catch (OverflowException)
        {
            return b > 0 ? long.MaxValue : long.MinValue;
        }
    }

    private static long SafeMultiplyByTen(long value)
    {
        try
        {
            return checked(value * 10);
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static long SafeNegate(long value) => value == long.MinValue ? long.MaxValue : -value;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var visualState = GetVisualState();
        GetColor(colorScheme, visualState, out var foreground, out var background);

        var prefix = string.IsNullOrEmpty(Label) ? string.Empty : Label + ": ";
        var line = new ConxelString(prefix, foreground, background);
        line.HighlightHotKey(HotConsoleKey, HotKeyChar, colorScheme.SelectedFocusedForeground, background);

        // Input well — shares TextBox's editable-field colours so inputs look consistent.
        var wellForeground = IsEnabled ? Color.Green : colorScheme.DisabledForeground;
        var wellBackground = !IsEnabled ? colorScheme.DisabledBackground : IsReadOnly ? Color.DarkYellow : Color.Yellow;

        var width = Math.Max(_minimum.ToString().Length, _maximum.ToString().Length);
        var valueText = _value.ToString();
        valueText = valueText.PadLeft(Math.Max(width, valueText.Length));

        line.Append(Border.Start, wellForeground, wellBackground);
        line.Append(valueText, wellForeground, wellBackground);
        line.Append(Border.End, wellForeground, wellBackground);

        var arrowForeground = IsReadOnly
            ? colorScheme.DisabledForeground
            : IsFocused ? colorScheme.SelectedFocusedForeground : foreground;
        _upArrowOffset = line.Count;
        line.Append(Border.ArrowUp, arrowForeground, background);
        _downArrowOffset = line.Count;
        line.Append(Border.ArrowDown, arrowForeground, background);

        if (line.Count < (int)ViewSize.Width)
        {
            line.Expand((int)ViewSize.Width, foreground, background);
        }

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        if (IsReadOnly)
        {
            visualState = VisualState.Disabled;
        }

        switch (visualState)
        {
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            case VisualState.Focused:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.Background;
                break;
            case VisualState.Normal:
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}
