using iConsole.Data;
using iTerminal.Core.Dialogs.Mail;

namespace iTerminal.Core.Commands;

public class MailDialogCommand(MailDialog mailDialog) : ShowDialogCommand<MailDialog>(mailDialog)
{
    public override string Title => "Mail";
    public override string Description => "Open the mail client: the inbox (POP3), sending (SMTP) and the address book (LDAP).";

    // Ctrl+W. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.W, false, false, true);
}
