using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class NetworkBrowserDialogCommand(NetworkBrowserDialog networkBrowserDialog) : ShowDialogCommand<NetworkBrowserDialog>(networkBrowserDialog)
{
    public override string Title => "Network Browser";
    public override string Description => "Browse and connect to SSH/SMB hosts on the local network.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.N, false, false, true);
}
