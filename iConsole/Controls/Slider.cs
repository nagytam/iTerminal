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

        double ratio = Math.Clamp((value - min) / (double)(max - min), 0.0, 1.0);
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
        return min + (long)Math.Round(ratio * (max - min));
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
                Value += Step;
                return true;
            case ConsoleKey.LeftArrow:
                Value -= Step;
                return true;
            case ConsoleKey.PageUp:
                Value += LargeStep;
                return true;
            case ConsoleKey.PageDown:
                Value -= LargeStep;
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
        if (IsReadOnly || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            Value += mouseEvent.WheelDelta > 0 ? Step : -Step;
            return true;
        }

        if (mouseEvent.LeftClicked)
        {
            int column = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
            Value = ValueAt(column, _minimum, _maximum, (int)ViewSize.Width);
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
        int thumb = ThumbColumn(_value, _minimum, _maximum, width);
        char[] track = new char[width];
        for (int i = 0; i < width; i++)
        {
            track[i] = i < thumb ? '━' : i == thumb ? '●' : '─';
        }

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, new ConxelString(new string(track), foreground, background));
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
