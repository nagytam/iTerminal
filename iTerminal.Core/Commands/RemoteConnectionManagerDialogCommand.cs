using iConsole.Data;
using iTerminal.Core.Dialogs.Remote;

namespace iTerminal.Core.Commands;

public class RemoteConnectionManagerDialogCommand(RemoteConnectionManagerDialog remoteConnectionManagerDialog) : ShowDialogCommand<RemoteConnectionManagerDialog>(remoteConnectionManagerDialog)
{
    public override string Title => "Remote connections";
    public override string Description => "Open the remote connection manager: saved SSH and PowerShell remoting hosts, each one a keypress from a live terminal session.";

    // Alt+F2. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F2, false, true, false);
}
