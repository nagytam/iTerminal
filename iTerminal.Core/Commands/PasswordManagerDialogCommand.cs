using iConsole.Data;
using iTerminal.Core.Dialogs.PasswordManager;

namespace iTerminal.Core.Commands;

public class PasswordManagerDialogCommand(PasswordManagerDialog passwordManagerDialog) : ShowDialogCommand<PasswordManagerDialog>(passwordManagerDialog)
{
    public override string Title => "Passwords";
    public override string Description => "Open the password manager: every saved network, mail and service password.";

    // Ctrl+X. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.X, false, false, true);
}
