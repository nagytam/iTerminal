using iConsole.Data;
using iTerminal.Core.Dialogs.Workspaces;

namespace iTerminal.Core.Commands;

public class WorkspaceManagerDialogCommand(WorkspaceManagerDialog workspaceManagerDialog) : ShowDialogCommand<WorkspaceManagerDialog>(workspaceManagerDialog)
{
    public override string Title => "Workspaces";
    public override string Description => "Open the workspace manager: save the screen layout (both panels, their sort order and the bottom bar) under a name, and switch back to any saved one.";

    // Ctrl+Shift+F2. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F2, true, false, true);
}
