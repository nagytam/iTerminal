using iConsole.Data;
using iTerminal.Core.Dialogs.PasswordManager;

namespace iTerminal.Core.Commands;

public class PasswordManagerDialogCommand(PasswordManagerDialog passwordManagerDialog) : ShowDialogCommand<PasswordManagerDialog>(passwordManagerDialog)
{
    public override string Title => "Passwords";
    public override string Description => "Open the password manager: every saved network, mail and service password.";

    // Ctrl+Shift+X. Key constructor order is (consoleKey, shift, alt, control).
    // Plain Ctrl+X is Cut in the file clipboard (Edit menu), so the password manager does not take it.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.X, true, false, true);
}
