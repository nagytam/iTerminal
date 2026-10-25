using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using System.Globalization;

namespace iConsole.Controls;

/// <summary>
/// A month grid for picking a date: a header with the month and year between two arrows, the weekday
/// names, and six rows of days (20 columns wide, 8 rows tall). Left/Right move a day, Up/Down a week,
/// PageUp/PageDown a month, Home/End the first and last day of the month, Enter raises
/// <see cref="DateActivated"/>. A click on a day selects it, a click on an arrow changes the month and
/// the wheel steps months. Days of the neighbouring months are drawn dim (or left blank with
/// <see cref="ShowAdjacentDays"/> off); today is picked out by colour. The selected day and today live in an
/// <see cref="ICalendarModel"/>: by default the control keeps its own, and a host that has such state hands it
/// in through <see cref="Model"/> so the grid and the host stay in step.
/// </summary>
public class Calendar : Control, IReadOnly
{
    public const int Columns = 20;
    public const int Rows = 8;

    /// <summary>Cells the ISO week-number column takes (two digits and a space).</summary>
    public const int WeekNumberWidth = 3;

    private ICalendarModel _model;
    private bool _showWeekNumbers;

    public Calendar(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(Columns, Rows);
        _model = new CalendarState();
        _model.Changed += OnModelChanged;
    }

    /// <summary>
    /// The date state shown and edited. Replacing it moves the subscription; the grid redraws whenever the
    /// model reports a change, whoever made it.
    /// </summary>
    public ICalendarModel Model
    {
        get => _model;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!ReferenceEquals(_model, value))
            {
                _model.Changed -= OnModelChanged;
                _model = value;
                _model.Changed += OnModelChanged;
                SelectedDateChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        SelectedDateChanged?.Invoke(this, EventArgs.Empty);
        StateHasChanged();
    }

    /// <summary>Draws an ISO week-number column ahead of the days and widens the control to fit it.</summary>
    public bool ShowWeekNumbers
    {
        get => _showWeekNumbers;
        set
        {
            _showWeekNumbers = value;
            ViewSize = new Size(Columns + GridX, Rows);
            StateHasChanged();
        }
    }

    /// <summary>Whether the days of the neighbouring months fill the first and last rows (dim); off leaves them blank.</summary>
    public bool ShowAdjacentDays { get; set; } = true;

    /// <summary>Draws the selected day in the focused colours even while the control does not hold the focus — for a host that handles the keys itself.</summary>
    public bool AlwaysEmphasizeSelection { get; set; }

    private int GridX => _showWeekNumbers ? WeekNumberWidth : 0;

    public bool IsReadOnly { get; set; }

    /// <summary>First column of the grid; Monday by default.</summary>
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    /// <summary>The date highlighted as today. Settable only while the control keeps its own model.</summary>
    public DateOnly Today
    {
        get => _model.Today;
        set
        {
            if (_model is CalendarState state)
            {
                state.Today = value;
            }
        }
    }

    public CultureInfo Culture { get; set; } = CultureInfo.InvariantCulture;

    public event EventHandler? SelectedDateChanged;

    /// <summary>Raised on Enter.</summary>
    public event EventHandler<DateOnly>? DateActivated;

    public DateOnly SelectedDate
    {
        get => _model.SelectedDate;
        set => _model.Select(value);
    }

    private DateOnly _selected => _model.SelectedDate;

    /// <summary>The first cell of the grid for the month containing <paramref name="date"/>.</summary>
    public static DateOnly GridStart(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        DateOnly first = new(date.Year, date.Month, 1);
        int back = ((int)first.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return AddDays(first, -back);
    }

    /// <summary>Moves a date by whole months, keeping the day where the target month has it (31 Jan + 1 → 28/29 Feb).</summary>
    public static DateOnly AddMonths(DateOnly date, int months)
    {
        try
        {
            return date.AddMonths(months);
        }
        catch (ArgumentOutOfRangeException)
        {
            return months < 0 ? DateOnly.MinValue : DateOnly.MaxValue;
        }
    }

    /// <summary>Moves a date by whole days, stopping at the first or last date that exists instead of throwing.</summary>
    public static DateOnly AddDays(DateOnly date, int days)
    {
        long target = (long)date.DayNumber + days;
        if (target <= DateOnly.MinValue.DayNumber)
        {
            return DateOnly.MinValue;
        }

        return target >= DateOnly.MaxValue.DayNumber ? DateOnly.MaxValue : DateOnly.FromDayNumber((int)target);
    }

    /// <summary>The date shown in grid cell (<paramref name="row"/>, <paramref name="column"/>) for a given selection.</summary>
    public DateOnly CellDate(int row, int column) => AddDays(GridStart(_selected, FirstDayOfWeek), row * 7 + column);

    /// <summary>The ISO week number of grid row <paramref name="row"/>, taken from the row's Thursday so any first weekday works.</summary>
    public int WeekNumberOfRow(int row)
    {
        for (int column = 0; column < 7; column++)
        {
            DateOnly day = CellDate(row, column);
            if (day.DayOfWeek == DayOfWeek.Thursday)
            {
                return ISOWeek.GetWeekOfYear(day.ToDateTime(TimeOnly.MinValue));
            }
        }

        return 0;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Arrows", "Move the selected day or week");
        yield return ("PageUp/PageDown", "Previous or next month");
        yield return ("Home/End", "First or last day of the month");
        yield return ("Enter/Space", "Choose the selected day");
        if (!IsReadOnly)
        {
            yield return ("T", "Select today");
            yield return ("Ctrl+PageUp/PageDown", "Previous or next year");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        if (keyEvent.Control && !keyEvent.Alt && !IsReadOnly
            && keyEvent.ConsoleKey is ConsoleKey.PageUp or ConsoleKey.PageDown)
        {
            SelectedDate = AddMonths(_selected, keyEvent.ConsoleKey == ConsoleKey.PageUp ? -12 : 12);
            return true;
        }

        if (!IsReadOnly && !keyEvent.Control && !keyEvent.Alt && keyEvent.KeyChar is 't' or 'T')
        {
            SelectedDate = Today;
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow when !IsReadOnly:
                SelectedDate = AddDays(_selected, -1);
                return true;
            case ConsoleKey.RightArrow when !IsReadOnly:
                SelectedDate = AddDays(_selected, 1);
                return true;
            case ConsoleKey.UpArrow when !IsReadOnly:
                SelectedDate = AddDays(_selected, -7);
                return true;
            case ConsoleKey.DownArrow when !IsReadOnly:
                SelectedDate = AddDays(_selected, 7);
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
            case ConsoleKey.Spacebar:
                DateActivated?.Invoke(this, _selected);
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
        int gridColumn = column - GridX;
        if (row == 0)
        {
            if (column == 0)
            {
                SelectedDate = AddMonths(_selected, -1);
            }
            else if (column == Columns + GridX - 1)
            {
                SelectedDate = AddMonths(_selected, 1);
            }
        }
        else if (row >= 2 && gridColumn >= 0 && gridColumn % 3 != 2 && gridColumn < Columns)
        {
            DateOnly day = CellDate(row - 2, gridColumn / 3);
            if (ShowAdjacentDays || day.Month == _selected.Month)
            {
                // A click on the day that is already chosen chooses it, as a second click on a table's cursor row activates it.
                bool wasSelected = day == _selected;
                SelectedDate = day;
                if (wasSelected)
                {
                    DateActivated?.Invoke(this, day);
                }
            }
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
        int space = Math.Max(0, Columns + GridX - 2 - title.Length);
        string header = "‹" + new string(' ', space / 2) + title + new string(' ', space - space / 2) + "›";
        WriteRow(buffer, 0, new ConxelString(header.PadRight(width), foreground, colorScheme.Background));

        ConxelString names = new(string.Empty, colorScheme.DisabledForeground, colorScheme.Background);
        if (_showWeekNumbers)
        {
            names.Append("Wk ", Color.Cyan, colorScheme.Background);
        }

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
            if (_showWeekNumbers)
            {
                line.Append(WeekNumberOfRow(r).ToString().PadLeft(2) + " ", colorScheme.DisabledForeground, colorScheme.Background);
            }

            for (int c = 0; c < 7; c++)
            {
                DateOnly day = CellDate(r, c);
                Color fg = day.Month != _selected.Month ? colorScheme.DisabledForeground : foreground;
                Color bg = colorScheme.Background;
                string text = day.Day.ToString().PadLeft(2);
                if (day.Month != _selected.Month && !ShowAdjacentDays)
                {
                    text = "  ";
                }
                else if (day == _selected)
                {
                    bool emphasize = IsFocused || AlwaysEmphasizeSelection;
                    fg = emphasize ? colorScheme.SelectedFocusedForeground : colorScheme.SelectedForeground;
                    bg = emphasize ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground;
                }
                else if (day == Today)
                {
                    fg = Color.Cyan;
                }

                line.Append(text, fg, bg);
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
