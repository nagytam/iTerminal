using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class DiskManagerDialogCommand(DiskManagerDialog diskManagerDialog) : ShowDialogCommand<DiskManagerDialog>(diskManagerDialog)
{
    public override string Title => "Disk manager";
    public override string Description => "Show available disks and disk details.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F9, false, true, false);
}
