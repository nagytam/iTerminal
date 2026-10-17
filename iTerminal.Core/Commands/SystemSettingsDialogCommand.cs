using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class SystemSettingsDialogCommand(SystemSettingsDialog systemSettingsDialog) : ShowDialogCommand<SystemSettingsDialog>(systemSettingsDialog)
{
    public override string Title => "System settings";
    public override string Description => "Change the computer name, workgroup, environment variables and time zone.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F10, true, false, true);
}
