using iConsole.Data;
using iTerminal.Core.Dialogs.Watch;

namespace iTerminal.Core.Commands;

public class WatchDialogCommand(WatchDialog watchDialog) : ShowDialogCommand<WatchDialog>(watchDialog)
{
    public override string Title => "Watch";
    public override string Description => "Open the watch: a world clock, alarms, a stopwatch and timers.";

    // Ctrl+Shift+T. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.T, true, false, true);
}
