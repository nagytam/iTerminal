using iConsole.Model;
using System.Globalization;

namespace iTerminal.Core.Model.Calendar;

/// <summary>
/// One week of a month grid: its ISO week number and seven days, Monday first. A day that belongs to
/// the neighbouring month is <c>null</c>, so the grid can be drawn without knowing where a month starts.
/// </summary>
public sealed record CalendarWeek(int WeekNumber, DateOnly?[] Days);

/// <summary>
/// The date state behind <see cref="Dialogs.Calendar.CalendarDialog"/>: which day is selected, which
/// day is today, and the month grid around the selection. Holds no UI and reads no clock, so the
/// dialog hands it today's date and every rule here can be tested on fixed dates.
///
/// <para>It is also the <see cref="ICalendarModel"/> the dialog's <see cref="iConsole.Controls.Calendar"/>
/// grid is bound to: the dialog moves the selection with its keys, a click on the grid selects through
/// <see cref="Select"/>, and <see cref="Changed"/> tells the other side — so there is one selected day, not
/// a copy in the grid that has to be kept in step.</para>
/// </summary>
public sealed class CalendarModel : ICalendarModel
{
    private static readonly DateOnly MinDate = DateOnly.MinValue;
    private static readonly DateOnly MaxDate = DateOnly.MaxValue;

    public CalendarModel(DateOnly today)
    {
        Today = today;
        SelectedDate = today;
    }

    private DateOnly _today;
    private DateOnly _selectedDate;

    public event EventHandler? Changed;

    public DateOnly Today
    {
        get => _today;
        private set => _today = value;
    }

    public DateOnly SelectedDate
    {
        get => _selectedDate;
        private set
        {
            if (_selectedDate != value)
            {
                _selectedDate = value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Selects <paramref name="date"/> (a click on the grid); the same day is not a change.</summary>
    public void Select(DateOnly date) => SelectedDate = date;

    /// <summary>Starts again from <paramref name="today"/>. Done each time the calendar is opened.</summary>
    public void Reset(DateOnly today)
    {
        var todayChanged = Today != today;
        Today = today;
        var before = SelectedDate;
        SelectedDate = today;
        if (todayChanged && before == today)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MoveDays(int days) => SelectedDate = AddDays(SelectedDate, days);

    /// <summary>Moves by months, keeping the day of the month where the target month has it, else its last day.</summary>
    public void MoveMonths(int months)
    {
        var total = SelectedDate.Year * 12 + (SelectedDate.Month - 1) + months;
        var year = (int)Math.Floor(total / 12.0);
        if (year < MinDate.Year)
        {
            SelectedDate = MinDate;
            return;
        }

        if (year > MaxDate.Year)
        {
            SelectedDate = MaxDate;
            return;
        }

        var month = total - year * 12 + 1;
        var day = Math.Min(SelectedDate.Day, DateTime.DaysInMonth(year, month));
        SelectedDate = new DateOnly(year, month, day);
    }

    public void MoveYears(int years) => MoveMonths(years * 12);

    public void GoToToday() => SelectedDate = Today;

    /// <summary>
    /// The month containing the selection, as whole weeks, Monday first. Six rows is the most a month can
    /// need; a month that needs five gets five.
    /// </summary>
    public IReadOnlyList<CalendarWeek> MonthGrid() => BuildGrid(SelectedDate.Year, SelectedDate.Month);

    /// <summary>Whole weeks of a month, Monday first, with the neighbouring months' days left out.</summary>
    public static IReadOnlyList<CalendarWeek> BuildGrid(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);

        // Monday is column 0. DayOfWeek.Monday is 1 and Sunday is 0, so Sunday maps to 6.
        var leadingBlanks = ((int)first.DayOfWeek + 6) % 7;
        var rowCount = (leadingBlanks + daysInMonth + 6) / 7;

        var weeks = new List<CalendarWeek>(rowCount);
        for (var row = 0; row < rowCount; row++)
        {
            var days = new DateOnly?[7];
            for (var column = 0; column < 7; column++)
            {
                var dayOfMonth = row * 7 + column - leadingBlanks + 1;
                days[column] = dayOfMonth >= 1 && dayOfMonth <= daysInMonth
                    ? new DateOnly(year, month, dayOfMonth)
                    : null;
            }

            // A week belongs to the year its Thursday falls in, so the ISO number comes from that day.
            var anchor = days.First(d => d.HasValue)!.Value;
            var thursday = anchor.AddDays(3 - ((int)anchor.DayOfWeek + 6) % 7);
            weeks.Add(new CalendarWeek(IsoWeek(thursday), days));
        }

        return weeks;
    }

    /// <summary>The ISO week number of a date (week 1 is the week with the year's first Thursday).</summary>
    public static int IsoWeek(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>"today", "in 3 days", "2 days ago" — the selection's distance from today.</summary>
    public static string RelativeTo(DateOnly date, DateOnly today)
    {
        var days = date.DayNumber - today.DayNumber;
        return days switch
        {
            0 => "today",
            1 => "tomorrow",
            -1 => "yesterday",
            > 1 => $"in {days} days",
            _ => $"{-days} days ago",
        };
    }

    private static DateOnly AddDays(DateOnly date, int days)
    {
        var target = (long)date.DayNumber + days;
        var clamped = Math.Clamp(target, MinDate.DayNumber, MaxDate.DayNumber);
        return DateOnly.FromDayNumber((int)clamped);
    }
}
