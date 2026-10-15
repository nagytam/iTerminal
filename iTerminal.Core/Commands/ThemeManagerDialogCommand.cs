using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class ThemeManagerDialogCommand(ThemeManagerDialog themeManagerDialog) : ShowDialogCommand<ThemeManagerDialog>(themeManagerDialog)
{
    public override string Title => "Themes";
    public override string Description => "Choose the colour theme.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F6, false, true, false);
}
