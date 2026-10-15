using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class RuntimeDependencyDialogCommand(RuntimeDependencyDialog runtimeDependencyDialog) : ShowDialogCommand<RuntimeDependencyDialog>(runtimeDependencyDialog)
{
    public override string Title => "Runtime Dependencies";
    public override string Description => "Install or update the components iTerminal downloads (Playwright and its Chromium).";

    // Reached from the Tools menu and the command palette; no hot key, so none of the menu's keys is taken.
    public override Key? Key { get; init; } = null;
}
