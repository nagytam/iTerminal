using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class MoveDialogCommand(
    MoveDialog moveDialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<MoveDialog>(moveDialog)
{
    public override string Title => "Move";
    public override string Description => "Move the marked files and folders, or the one under the cursor, to another location.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F6, true, false, false);

    public override bool CanExecute()
    {
        return FocusedEntry.ActionableUrls(SourceModel).Count > 0;
    }

    /// <summary>The panel Shift+F6 acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    protected override void OnBeforeShow(MoveDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        dialog.SourceModel = SourceModel;
    }
}
