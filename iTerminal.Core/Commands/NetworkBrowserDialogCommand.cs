using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class NetworkBrowserDialogCommand(NetworkBrowserDialog networkBrowserDialog) : ShowDialogCommand<NetworkBrowserDialog>(networkBrowserDialog)
{
    public override string Title => "Network Browser";
    public override string Description => "Browse and connect to SSH/SMB hosts on the local network.";

    // Alt+Shift+F8. Not Ctrl+N: that is the Navigate menu's hot key, which a file panel must open.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F8, true, true, false);
}
