using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A one-line horizontal slider (Terminal.Gui's LinearRange): a track with a thumb for picking a value in
/// [<see cref="Minimum"/>, <see cref="Maximum"/>]. Left/Right step by <see cref="Step"/>, PageUp/PageDown by
/// <see cref="LargeStep"/>, Home/End jump to the bounds, and clicking the track moves the thumb there.
/// </summary>
public class Slider : Control, IReadOnly
{
    private long _value;
    private long _minimum;
    private long _maximum = 100;

    public Slider(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(20, 1);
    }

    public bool IsReadOnly { get; set; }
    public long Step { get; set; } = 1;
    public long LargeStep { get; set; } = 10;

    /// <summary>Draws the current value right of the track, so the number is readable without counting cells.</summary>
    public bool ShowValue { get; set; }

    private int ValueColumns => ShowValue ? Math.Max(_maximum.ToString().Length, _minimum.ToString().Length) + 1 : 0;

    private int TrackWidth => Math.Max(1, (int)ViewSize.Width - ValueColumns);

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

            Value = _value;
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

            Value = _value;
        }
    }

    public long Value
    {
        get => _value;
        set
        {
            long clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value != clamped)
            {
                _value = clamped;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    /// <summary>Column of the thumb on a track of <paramref name="width"/> cells.</summary>
    public static int ThumbColumn(long value, long min, long max, int width)
    {
        if (width <= 1 || max <= min)
        {
            return 0;
        }

        double ratio = Math.Clamp(((double)value - min) / ((double)max - min), 0.0, 1.0);
        return (int)Math.Round(ratio * (width - 1));
    }

    /// <summary>The value a thumb at <paramref name="column"/> of a track <paramref name="width"/> cells wide stands for.</summary>
    public static long ValueAt(int column, long min, long max, int width)
    {
        if (width <= 1 || max <= min)
        {
            return min;
        }

        double ratio = Math.Clamp(column / (double)(width - 1), 0.0, 1.0);
        double offset = Math.Round(ratio * ((double)max - min));
        return offset >= (double)max - min ? max : min + (long)offset;
    }

    /// <summary>Adds without wrapping around: a step past <see cref="long.MaxValue"/> stays there instead of jumping to the minimum.</summary>
    private static long SaturatingAdd(long value, long delta)
    {
        long sum = unchecked(value + delta);
        if (delta > 0 && sum < value)
        {
            return long.MaxValue;
        }

        return delta < 0 && sum > value ? long.MinValue : sum;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (IsReadOnly || !IsEnabled)
        {
            yield break;
        }

        yield return ("Left/Right", "Lower or raise the value");
        yield return ("Up/Down", "Lower or raise the value");
        yield return ("PageUp/PageDown", "Raise or lower the value by a large step");
        yield return ("Home/End", "Set the minimum or maximum");
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
            case ConsoleKey.UpArrow:
                Value = SaturatingAdd(_value, Step);
                return true;
            case ConsoleKey.LeftArrow:
            case ConsoleKey.DownArrow:
                Value = SaturatingAdd(_value, -Step);
                return true;
            case ConsoleKey.PageUp:
                Value = SaturatingAdd(_value, LargeStep);
                return true;
            case ConsoleKey.PageDown:
                Value = SaturatingAdd(_value, -LargeStep);
                return true;
            case ConsoleKey.Home:
                Value = _minimum;
                return true;
            case ConsoleKey.End:
                Value = _maximum;
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
            Value = SaturatingAdd(_value, mouseEvent.WheelDelta > 0 ? Step : -Step);
            return true;
        }

        if (mouseEvent.LeftClicked)
        {
            if (!IsFocused)
            {
                SetFocus();
            }

            int column = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
            Value = ValueAt(Math.Min(column, TrackWidth - 1), _minimum, _maximum, TrackWidth);
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
        GetColor(colorScheme, GetVisualState(), out Color foreground, out Color background);

        int width = (int)ViewSize.Width;
        int trackWidth = TrackWidth;
        int thumb = ThumbColumn(_value, _minimum, _maximum, trackWidth);
        char[] track = new char[trackWidth];
        for (int i = 0; i < trackWidth; i++)
        {
            track[i] = i < thumb ? '━' : i == thumb ? '●' : '─';
        }

        string line = new string(track);
        if (ShowValue)
        {
            line = (line + " " + _value.ToString().PadLeft(ValueColumns - 1)).PadRight(width);
        }

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, new ConxelString(line, foreground, background));
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
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}
