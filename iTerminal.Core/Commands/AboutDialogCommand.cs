using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class AboutDialogCommand(AboutDialog aboutDialog) : ShowDialogCommand<AboutDialog>(aboutDialog)
{
    public override string Title => "About";
    public override string Description => "Show application information and version details.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F1, true, true, true);
}
