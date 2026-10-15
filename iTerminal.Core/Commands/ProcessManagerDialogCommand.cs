using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class ProcessManagerDialogCommand(ProcessManagerDialog processManagerDialog) : ShowDialogCommand<ProcessManagerDialog>(processManagerDialog)
{
    public override string Title => "Process manager";
    public override string Description => "Show running processes.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F12, false, true, false);
}
