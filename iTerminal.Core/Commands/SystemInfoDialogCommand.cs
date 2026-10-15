using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class SystemInfoDialogCommand(SystemInfoDialog systemInfoDialog) : ShowDialogCommand<SystemInfoDialog>(systemInfoDialog)
{
    public override string Title => "System information";
    public override string Description => "Show system information for the current machine.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F11, false, false, true);
}
