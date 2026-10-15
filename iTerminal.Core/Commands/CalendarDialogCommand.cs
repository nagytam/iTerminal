using iConsole.Data;
using iTerminal.Core.Dialogs.Calendar;

namespace iTerminal.Core.Commands;

public class CalendarDialogCommand(CalendarDialog calendarDialog) : ShowDialogCommand<CalendarDialog>(calendarDialog)
{
    public override string Title => "Calendar";
    public override string Description => "Open the month calendar, with the day's meetings.";

    // Ctrl+D. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.D, false, false, true);
}
