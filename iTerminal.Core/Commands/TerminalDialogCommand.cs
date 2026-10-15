using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class TerminalDialogCommand(TerminalDialog terminalDialog) : ShowDialogCommand<TerminalDialog>(terminalDialog)
{
    public override string Title => "Terminal";
    public override string Description => "Open the terminal dialog.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F2, false, false, false);
}
