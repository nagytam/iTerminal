using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class SettingsDialogCommand(SettingsDialog settingsDialog) : ShowDialogCommand<SettingsDialog>(settingsDialog)
{
    public override string Title => "Settings";
    public override string Description => "Open application settings.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F8, true, false, false);
}
