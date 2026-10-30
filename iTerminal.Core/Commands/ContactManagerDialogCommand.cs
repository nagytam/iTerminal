using iConsole.Data;
using iTerminal.Core.Dialogs.Contacts;

namespace iTerminal.Core.Commands;

public class ContactManagerDialogCommand(ContactManagerDialog contactManagerDialog) : ShowDialogCommand<ContactManagerDialog>(contactManagerDialog)
{
    public override string Title => "Contacts";
    public override string Description => "Open the contact manager: names, email addresses, phone numbers and companies, with a filter.";

    // Ctrl+Shift+K. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.K, true, false, true);
}
