using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class RuntimeDependencyDialogCommand(RuntimeDependencyDialog runtimeDependencyDialog) : ShowDialogCommand<RuntimeDependencyDialog>(runtimeDependencyDialog)
{
    public override string Title => "Runtime Dependencies";
    public override string Description => "Install or update the components iTerminal downloads (Playwright and its Chromium).";

    // Reached from the Tools menu and the command palette; no hot key, so none of the menu's keys is taken.
    public override Key? Key { get; init; } = null;

    /// <summary>Only when a dependency is missing or can be updated: the dialog is not shown when there is nothing to do.</summary>
    public override bool CanExecute() => Dialog.HasWork;
}
