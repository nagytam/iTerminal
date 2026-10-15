using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class NetworkManagerDialogCommand(NetworkManagerDialog networkManagerDialog) : ShowDialogCommand<NetworkManagerDialog>(networkManagerDialog)
{
    public override string Title => "Network manager";
    public override string Description => "Show network interfaces and related details.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F5, false, false, true);
}
