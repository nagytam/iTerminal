using iConsole.Data;
using iTerminal.Core.Dialogs.PackageManager;

namespace iTerminal.Core.Commands;

public class PackageManagerDialogCommand(PackageManagerDialog packageManagerDialog) : ShowDialogCommand<PackageManagerDialog>(packageManagerDialog)
{
    public override string Title => "Packages";
    public override string Description => "Open the package manager (list, install, uninstall applications).";

    // Alt+F8. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F8, false, true, false);
}
