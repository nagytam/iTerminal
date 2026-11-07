using iConsole.Data;
using iTerminal.Core.Dialogs.EnvVars;

namespace iTerminal.Core.Commands;

public class EnvironmentVariablesDialogCommand(EnvironmentVariablesDialog environmentVariablesDialog) : ShowDialogCommand<EnvironmentVariablesDialog>(environmentVariablesDialog)
{
    public override string Title => "Environment variables";
    public override string Description => "View, add, change and delete environment variables for this session, the user or the machine.";

    // Ctrl+Shift+E. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.E, true, false, true);
}
