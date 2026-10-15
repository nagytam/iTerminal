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

    /// <summary>
    /// Ctrl+Shift+X. In a file panel Ctrl+X cuts the file under the cursor to the clipboard and only
    /// falls through to this command on the ".." row, so the chord that always reaches it is this one.
    /// </summary>
    public Key AlternateKey { get; init; } = new(ConsoleKey.X, true, false, true);

    public override string KeyDisplayText => $"{FormatKey(Key)} / {FormatKey(AlternateKey)}";

    public override Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (AlternateKey.IsIdentical(keyEvent))
        {
            return RunAsync(null);
        }

        return base.HandleKeyEventAsync(keyEvent);
    }
}
