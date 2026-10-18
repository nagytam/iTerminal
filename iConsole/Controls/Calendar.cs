using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using System.Globalization;

namespace iConsole.Controls;

/// <summary>
/// A month grid for picking a date: a header with the month and year between two arrows, the weekday
/// names, and six rows of days (20 columns wide, 8 rows tall). Left/Right move a day, Up/Down a week,
/// PageUp/PageDown a month, Home/End the first and last day of the month, Enter raises
/// <see cref="DateActivated"/>. A click on a day selects it, a click on an arrow changes the month and
/// the wheel steps months. Days of the neighbouring months are drawn dim; today is underlined by colour.
/// </summary>
public class Calendar : Control, IReadOnly
{
    public const int Columns = 20;
    public const int Rows = 8;

    private DateOnly _selected = DateOnly.FromDateTime(DateTime.Today);

    public Calendar(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(Columns, Rows);
    }

    public bool IsReadOnly { get; set; }

    /// <summary>First column of the grid; Monday by default.</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>The date highlighted as today.</summary>
    public DateOnly Today { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    public event EventHandler? SelectedDateChanged;

    /// <summary>Raised on Enter.</summary>
    public event EventHandler<DateOnly>? DateActivated;

    public DateOnly SelectedDate
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                SelectedDateChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    /// <summary>The first cell of the grid for the month containing <paramref name="date"/>.</summary>
    public static DateOnly GridStart(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        DateOnly first = new(date.Year, date.Month, 1);
        int back = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return first.AddDays(-back);
    }

    /// <summary>Moves a date by whole months, keeping the day where the target month has it (31 Jan + 1 → 28/29 Feb).</summary>
    public static DateOnly AddMonths(DateOnly date, int months) => date.AddMonths(months);

    /// <summary>The date shown in grid cell (<paramref name="row"/>, <paramref name="column"/>) for a given selection.</summary>
    public DateOnly CellDate(int row, int column) => GridStart(_selected, FirstDayOfWeek).AddDays(row * 7 + column);

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow when !IsReadOnly:
                SelectedDate = _selected.AddDays(-1);
                return true;
            case ConsoleKey.RightArrow when !IsReadOnly:
                SelectedDate = _selected.AddDays(1);
                return true;
            case ConsoleKey.UpArrow when !IsReadOnly:
                SelectedDate = _selected.AddDays(-7);
                return true;
            case ConsoleKey.DownArrow when !IsReadOnly:
                SelectedDate = _selected.AddDays(7);
                return true;
            case ConsoleKey.PageUp when !IsReadOnly:
                SelectedDate = AddMonths(_selected, -1);
                return true;
            case ConsoleKey.PageDown when !IsReadOnly:
                SelectedDate = AddMonths(_selected, 1);
                return true;
            case ConsoleKey.Home when !IsReadOnly:
                SelectedDate = new DateOnly(_selected.Year, _selected.Month, 1);
                return true;
            case ConsoleKey.End when !IsReadOnly:
                SelectedDate = new DateOnly(_selected.Year, _selected.Month, DateTime.DaysInMonth(_selected.Year, _selected.Month));
                return true;
            case ConsoleKey.Enter:
                DateActivated?.Invoke(this, _selected);
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
            SelectedDate = AddMonths(_selected, mouseEvent.WheelDelta > 0 ? -1 : 1);
            return true;
        }

        if (!mouseEvent.LeftClicked)
        {
            return isHandled;
        }

        if (!IsFocused)
        {
            SetFocus();
        }

        int column = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
        int row = (int)mouseEvent.Position.Y - (int)AbsolutePosition.Y;
        if (row == 0)
        {
            if (column == 0)
            {
                SelectedDate = AddMonths(_selected, -1);
            }
            else if (column == Columns - 1)
            {
                SelectedDate = AddMonths(_selected, 1);
            }
        }
        else if (row >= 2 && column % 3 != 2 && column < 20)
        {
            SelectedDate = CellDate(row - 2, column / 3);
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
        Color foreground = IsEnabled ? colorScheme.Foreground : colorScheme.DisabledForeground;

        string title = _selected.ToString("MMMM yyyy", Culture);
        int space = Math.Max(0, Columns - 2 - title.Length);
        string header = "‹" + new string(' ', space / 2) + title + new string(' ', space - space / 2) + "›";
        WriteRow(buffer, 0, new ConxelString(header.PadRight(width), foreground, colorScheme.Background));

        ConxelString names = new(string.Empty, colorScheme.DisabledForeground, colorScheme.Background);
        for (int c = 0; c < 7; c++)
        {
            string name = Culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)(((int)FirstDayOfWeek + c) % 7));
            names.Append(name[..Math.Min(2, name.Length)].PadRight(2) + (c < 6 ? " " : string.Empty), colorScheme.DisabledForeground, colorScheme.Background);
        }

        names.Expand(width, colorScheme.Foreground, colorScheme.Background);
        WriteRow(buffer, 1, names);

        for (int r = 0; r < 6; r++)
        {
            ConxelString line = new(string.Empty, foreground, colorScheme.Background);
            for (int c = 0; c < 7; c++)
            {
                DateOnly day = CellDate(r, c);
                Color fg = day.Month != _selected.Month ? colorScheme.DisabledForeground : foreground;
                Color bg = colorScheme.Background;
                if (day == _selected)
                {
                    fg = IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.SelectedForeground;
                    bg = IsFocused ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground;
                }
                else if (day == Today)
                {
                    fg = Color.Cyan;
                }

                line.Append(day.Day.ToString().PadLeft(2), fg, bg);
                if (c < 6)
                {
                    line.Append(" ", foreground, colorScheme.Background);
                }
            }

            line.Expand(width, foreground, colorScheme.Background);
            WriteRow(buffer, 2 + r, line);
        }
    }

    private void WriteRow(IBuffer buffer, int row, ConxelString line)
        => buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)row), new Size(ViewSize.Width, 1), line);
}
