using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Model.Calendar;

namespace iTerminal.Core.Dialogs.Calendar;

/// <summary>
/// A month calendar with the selected day's meetings under it. The arrows move the selection by day
/// and week, PgUp/PgDn by month and Ctrl+PgUp/PgDn by year. N (or Enter) types a new meeting for the
/// selected day as "HH:mm [minutes] title"; Alt+Up/Down choose one of the day's meetings and D removes it.
/// Meetings are kept by <see cref="MeetingStore"/>, so they survive a restart.
/// </summary>
public class CalendarDialog : Dialog
{
    private const int DialogWidth = 72;
    private const int DialogHeight = 25;
    private const int WeekNumberWidth = 3;
    private const int GridCellWidth = 4;
    private const int GridWidth = WeekNumberWidth + 1 + 7 * GridCellWidth;
    private const int MeetingRows = 4;
    private const int MaxEntryLength = 80;

    private readonly IDateTimeProviderService _dateTimeProviderService;
    private readonly MeetingStore _meetings;
    private readonly CalendarModel _calendar;

    /// <summary>What is being typed for a new meeting, or null when no entry is open.</summary>
    private string? _entry;
    private string? _entryError;

    /// <summary>The highlighted meeting in the selected day's list.</summary>
    private int _meetingCursor;
    private DateOnly _cursorDate;

    public CalendarDialog(IDateTimeProviderService dateTimeProviderService, MeetingStore meetings, IThemeService themeService)
        : base(nameof(CalendarDialog), themeService)
    {
        Title = "Calendar";
        _dateTimeProviderService = dateTimeProviderService;
        _meetings = meetings;
        _calendar = new CalendarModel(Today());
        _cursorDate = _calendar.SelectedDate;
    }

    private DateOnly Today() => DateOnly.FromDateTime(_dateTimeProviderService.Now);

    /// <summary>
    /// The dialog is built once and opened many times, so "today" is read on each opening and the
    /// selection starts there again rather than where the last visit left it.
    /// </summary>
    public override async Task ShowAsync()
    {
        _calendar.Reset(Today());
        _entry = null;
        _entryError = null;
        SyncCursorToSelectedDate();
        await base.ShowAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        base.OnInitializeLayout();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_entry is not null)
        {
            return HandleEntryKey(keyEvent);
        }

        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        var alt = keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift;
        var control = keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow when plain:
                return MoveDays(-1);
            case ConsoleKey.RightArrow when plain:
                return MoveDays(1);
            case ConsoleKey.UpArrow when plain:
                return MoveDays(-7);
            case ConsoleKey.DownArrow when plain:
                return MoveDays(7);
            case ConsoleKey.UpArrow when alt:
                return MoveMeetingCursor(-1);
            case ConsoleKey.DownArrow when alt:
                return MoveMeetingCursor(1);
            case ConsoleKey.PageUp when control:
                return MoveYears(-1);
            case ConsoleKey.PageDown when control:
                return MoveYears(1);
            case ConsoleKey.PageUp when plain:
                return MoveMonths(-1);
            case ConsoleKey.PageDown when plain:
                return MoveMonths(1);
            case ConsoleKey.Home when plain:
                _calendar.GoToToday();
                return Moved();
            case ConsoleKey.Enter when plain:
            case ConsoleKey.Insert when plain:
                OpenEntry();
                return true;
            case ConsoleKey.Delete when plain:
                DeleteHighlightedMeeting();
                return true;
        }

        if (plain && char.ToLowerInvariant(keyEvent.KeyChar) == 'n')
        {
            OpenEntry();
            return true;
        }

        if (plain && char.ToLowerInvariant(keyEvent.KeyChar) == 'd')
        {
            DeleteHighlightedMeeting();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// While a meeting is being typed, every key belongs to the entry — the arrows must not move the
    /// calendar under it. Esc abandons the entry rather than closing the dialog.
    /// </summary>
    private bool HandleEntryKey(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _entry = null;
                _entryError = null;
                break;
            case ConsoleKey.Enter:
                CommitEntry();
                break;
            case ConsoleKey.Backspace when _entry!.Length > 0:
                _entry = _entry[..^1];
                _entryError = null;
                break;
            default:
                var printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && _entry!.Length < MaxEntryLength)
                {
                    _entry += keyEvent.KeyChar;
                    _entryError = null;
                }
                break;
        }

        StateHasChanged();
        return true;
    }

    private void OpenEntry()
    {
        _entry = string.Empty;
        _entryError = null;
        StateHasChanged();
    }

    private void CommitEntry()
    {
        var date = _calendar.SelectedDate;
        if (Meeting.TryParse(_entry!, date, out var meeting, out var error) && meeting is not null)
        {
            _meetings.Add(meeting);
            _entry = null;
            _entryError = null;
            var index = _meetings.On(date).ToList().IndexOf(meeting);
            _meetingCursor = Math.Max(0, index);
        }
        else
        {
            _entryError = error;
        }
    }

    private void DeleteHighlightedMeeting()
    {
        var list = _meetings.On(_calendar.SelectedDate);
        if (_meetingCursor < 0 || _meetingCursor >= list.Count)
        {
            return;
        }

        _meetings.Remove(list[_meetingCursor]);
        _meetingCursor = Math.Clamp(_meetingCursor, 0, Math.Max(0, list.Count - 2));
        StateHasChanged();
    }

    private bool MoveDays(int days)
    {
        _calendar.MoveDays(days);
        return Moved();
    }

    private bool MoveMonths(int months)
    {
        _calendar.MoveMonths(months);
        return Moved();
    }

    private bool MoveYears(int years)
    {
        _calendar.MoveYears(years);
        return Moved();
    }

    private bool MoveMeetingCursor(int delta)
    {
        var count = _meetings.On(_calendar.SelectedDate).Count;
        _meetingCursor = Math.Clamp(_meetingCursor + delta, 0, Math.Max(0, count - 1));
        StateHasChanged();
        return true;
    }

    private bool Moved()
    {
        SyncCursorToSelectedDate();
        StateHasChanged();
        return true;
    }

    /// <summary>A different day has a different list, so the highlight goes back to its first meeting.</summary>
    private void SyncCursorToSelectedDate()
    {
        if (_calendar.SelectedDate != _cursorDate)
        {
            _cursorDate = _calendar.SelectedDate;
            _meetingCursor = 0;
        }
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        for (var row = 0; row < ih; row++)
        {
            Put(buffer, 0, row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        }

        DrawMonth(buffer, iw, colorScheme);
        DrawSelectedDay(buffer, iw, colorScheme);
        DrawMeetings(buffer, iw, colorScheme);
        DrawHints(buffer, iw, ih, colorScheme);
    }

    private void DrawMonth(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var selected = _calendar.SelectedDate;
        Put(buffer, 0, 0, iw, Center(selected.ToString("MMMM yyyy"), iw), colorScheme.Foreground, colorScheme.Background);

        var gridX = Math.Max(0, (iw - GridWidth) / 2);
        var cellsX = gridX + WeekNumberWidth + 1;

        Put(buffer, gridX, 2, WeekNumberWidth + 1, "Wk ", Color.Cyan, colorScheme.Background);
        string[] weekdays = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];
        for (var column = 0; column < 7; column++)
        {
            Put(buffer, cellsX + column * GridCellWidth, 2, GridCellWidth, weekdays[column] + "  ", Color.Cyan, colorScheme.Background);
        }

        var weeks = _calendar.MonthGrid();
        for (var row = 0; row < weeks.Count; row++)
        {
            var week = weeks[row];
            var y = 3 + row;
            Put(buffer, gridX, y, WeekNumberWidth, $"{week.WeekNumber,2}", colorScheme.DisabledForeground, colorScheme.Background);

            for (var column = 0; column < 7; column++)
            {
                var day = week.Days[column];
                var x = cellsX + column * GridCellWidth;
                if (day is null)
                {
                    Put(buffer, x, y, GridCellWidth, string.Empty, colorScheme.Foreground, colorScheme.Background);
                    continue;
                }

                var text = $"{day.Value.Day,2}  ";
                if (day.Value == selected)
                {
                    Put(buffer, x, y, GridCellWidth, text, colorScheme.SelectedFocusedForeground, colorScheme.SelectedFocusedBackground);
                }
                else if (day.Value == _calendar.Today)
                {
                    Put(buffer, x, y, GridCellWidth, text, Color.Cyan, colorScheme.Background);
                }
                else
                {
                    Put(buffer, x, y, GridCellWidth, text, colorScheme.Foreground, colorScheme.Background);
                }
            }
        }
    }

    private void DrawSelectedDay(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var selected = _calendar.SelectedDate;
        var daysInYear = DateTime.IsLeapYear(selected.Year) ? 366 : 365;
        var week = CalendarModel.IsoWeek(selected);
        var relative = CalendarModel.RelativeTo(selected, _calendar.Today);

        Put(buffer, 0, 10, iw, Center(selected.ToString("dddd, d MMMM yyyy"), iw), colorScheme.SelectedFocusedForeground, colorScheme.Background);
        Put(buffer, 0, 11, iw, Center($"Week {week}  |  day {selected.DayOfYear} of {daysInYear}  |  {relative}", iw), colorScheme.Foreground, colorScheme.Background);
    }

    private void DrawMeetings(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var list = _meetings.On(_calendar.SelectedDate);
        var header = list.Count == 0 ? "No meetings on this day" : $"Meetings ({list.Count})";
        Put(buffer, 1, 13, iw - 2, header, Color.Cyan, colorScheme.Background);

        var first = _meetingCursor >= MeetingRows ? _meetingCursor - MeetingRows + 1 : 0;
        for (var i = 0; i < MeetingRows; i++)
        {
            var index = first + i;
            var y = 14 + i;
            var remaining = list.Count - (first + MeetingRows);
            if (i == MeetingRows - 1 && remaining > 0)
            {
                Put(buffer, 1, y, iw - 2, $"… {remaining + 1} more", colorScheme.DisabledForeground, colorScheme.Background);
                continue;
            }

            if (index >= list.Count)
            {
                Put(buffer, 1, y, iw - 2, string.Empty, colorScheme.Foreground, colorScheme.Background);
                continue;
            }

            var highlighted = index == _meetingCursor;
            var text = (highlighted ? "> " : "  ") + list[index].Describe();
            var foreground = highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            var background = highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
            Put(buffer, 1, y, iw - 2, text, foreground, background);
        }

        if (_entry is not null)
        {
            Put(buffer, 1, 18, iw - 2, $"New meeting: {_entry}_", colorScheme.SelectedFocusedForeground, colorScheme.Background);
            if (_entryError is not null)
            {
                Put(buffer, 1, 19, iw - 2, _entryError, colorScheme.Error, colorScheme.Background);
            }
        }
        else
        {
            Put(buffer, 1, 18, iw - 2, "N: add a meeting, e.g. 14:30 45 Standup", colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private void DrawHints(IBuffer buffer, int iw, int ih, ColorScheme colorScheme)
    {
        var first = ih - 2;
        Put(buffer, 1, first, iw - 2, "Arrows day/week  PgUp/PgDn month  Ctrl+PgUp/PgDn year", colorScheme.Foreground, colorScheme.Background);
        Put(buffer, 1, first + 1, iw - 2, "Home today  N add  Alt+Up/Down choose  D remove  Esc close", colorScheme.Foreground, colorScheme.Background);
    }

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        var clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }

    private static string Center(string text, int width) =>
        text.Length >= width ? text[..width] : text.PadLeft((width + text.Length) / 2).PadRight(width);

    #region Test seams

    internal CalendarModel CalendarForTest => _calendar;
    internal string? EntryForTest => _entry;
    internal string? EntryErrorForTest => _entryError;
    internal int MeetingCursorForTest => _meetingCursor;

    #endregion
}
