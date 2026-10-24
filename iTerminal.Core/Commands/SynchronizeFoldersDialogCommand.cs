using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>Opens the folder synchroniser for the two panels. Reached from the Edit menu; no hot key.</summary>
public class SynchronizeFoldersDialogCommand(SynchronizeFoldersDialog dialog) : ShowDialogCommand<SynchronizeFoldersDialog>(dialog)
{
    public override string Title => "Synchronise folders";
    public override string Description => "Compare the two panels' folders and copy the differences across, after showing what would change.";

    public override Key? Key { get; init; } = null;
}
