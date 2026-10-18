using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A time-of-day field drawn as <c>[14:05]</c> (or <c>[14:05:30]</c> with <see cref="ShowSeconds"/>).
/// Left/Right (or Tab-like Enter) switch between hours, minutes and seconds; Up/Down step the field and wrap
/// within it; digits type a value (two digits, then the next field is selected); the wheel and a click on a
/// field work as expected. <see cref="Value"/> is a <see cref="TimeOnly"/>.
/// </summary>
public class TimePicker : Control, IReadOnly
{
    private int _hour;
    private int _minute;
    private int _second;
    private int _field;
    private int _typed;
    private bool _showSeconds;

    public TimePicker(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(8, 1);
    }

    public bool IsReadOnly { get; set; }

    public event EventHandler? ValueChanged;

    public bool ShowSeconds
    {
        get => _showSeconds;
        set
        {
            _showSeconds = value;
            _field = Math.Min(_field, FieldCount - 1);
            ViewSize = new Size(FieldCount == 3 ? 10 : 7, 1);
            StateHasChanged();
        }
    }

    private int FieldCount => _showSeconds ? 3 : 2;

    /// <summary>The active field: 0 hours, 1 minutes, 2 seconds.</summary>
    public int ActiveField => _field;

    public TimeOnly Value
    {
        get => new(_hour, _minute, _showSeconds ? _second : 0);
        set
        {
            if (_hour != value.Hour || _minute != value.Minute || _second != value.Second)
            {
                _hour = value.Hour;
                _minute = value.Minute;
                _second = value.Second;
                ValueChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    private static int MaxOf(int field) => field == 0 ? 23 : 59;

    private int Get(int field) => field switch { 0 => _hour, 1 => _minute, _ => _second };

    private void Set(int field, int value)
    {
        int clamped = Math.Clamp(value, 0, MaxOf(field));
        if (Get(field) == clamped)
        {
            return;
        }

        switch (field)
        {
            case 0:
                _hour = clamped;
                break;
            case 1:
                _minute = clamped;
                break;
            default:
                _second = clamped;
                break;
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
        StateHasChanged();
    }

    /// <summary>Steps field <paramref name="field"/> by <paramref name="delta"/>, wrapping within its range.</summary>
    public static int Wrap(int value, int delta, int max) => ((value + delta) % (max + 1) + max + 1) % (max + 1);

    private void Step(int delta) => Set(_field, Wrap(Get(_field), delta, MaxOf(_field)));

    private void MoveField(int field)
    {
        _field = Math.Clamp(field, 0, FieldCount - 1);
        _typed = 0;
        StateHasChanged();
    }

    private void TypeDigit(int digit)
    {
        int max = MaxOf(_field);
        if (_typed == 0)
        {
            Set(_field, digit);
            _typed = 1;
            if (digit * 10 > max)
            {
                CompleteField();
            }
        }
        else
        {
            Set(_field, Get(_field) * 10 + digit);
            CompleteField();
        }
    }

    private void CompleteField()
    {
        _typed = 0;
        if (_field < FieldCount - 1)
        {
            _field++;
        }

        StateHasChanged();
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
            case ConsoleKey.UpArrow:
                Step(1);
                return true;
            case ConsoleKey.DownArrow:
                Step(-1);
                return true;
            case ConsoleKey.PageUp:
                Step(10);
                return true;
            case ConsoleKey.PageDown:
                Step(-10);
                return true;
            case ConsoleKey.LeftArrow:
                MoveField(_field - 1);
                return true;
            case ConsoleKey.RightArrow:
                MoveField(_field + 1);
                return true;
        }

        if (keyEvent.KeyChar is >= '0' and <= '9')
        {
            TypeDigit(keyEvent.KeyChar - '0');
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
            Step(mouseEvent.WheelDelta > 0 ? 1 : -1);
            return true;
        }

        if (mouseEvent.LeftClicked)
        {
            int column = (int)mouseEvent.Position.X - (int)AbsolutePosition.X - 1;
            if (!IsFocused)
            {
                SetFocus();
            }

            MoveField(Math.Clamp(column / 3, 0, FieldCount - 1));
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
        bool disabled = IsReadOnly || !IsEnabled;
        Color wellForeground = disabled ? colorScheme.DisabledForeground : Color.Green;
        Color wellBackground = disabled ? colorScheme.DisabledBackground : Color.Yellow;
        ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
        line.Append(Border.Start, wellForeground, wellBackground);
        for (int f = 0; f < FieldCount; f++)
        {
            if (f > 0)
            {
                line.Append(":", wellForeground, wellBackground);
            }

            bool active = f == _field && IsFocused && !disabled;
            line.Append(Get(f).ToString("00"),
                active ? colorScheme.SelectedFocusedForeground : wellForeground,
                active ? colorScheme.SelectedFocusedBackground : wellBackground);
        }

        line.Append(Border.End, wellForeground, wellBackground);
        line.Expand((int)ViewSize.Width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
