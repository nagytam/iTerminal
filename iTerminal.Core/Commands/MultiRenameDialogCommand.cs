using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Commands;

/// <summary>
/// Opens <see cref="MultiRenameDialog"/> over the marked entries, or the one under the cursor. Reached
/// from the Edit menu only: a chord would collide with another command on legacy Linux terminals
/// (Ctrl+Shift+Y sends the same byte as Ctrl+Y), so none is bound.
/// </summary>
public class MultiRenameDialogCommand(
    MultiRenameDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<MultiRenameDialog>(dialog)
{
    public override string Title => "Multi-rename";
    public override string Description => "Rename the marked files and folders, or the one under the cursor, by a pattern.";

    /// <summary>The panel the dialog acts on: the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    public override bool CanExecute()
    {
        // A read-only drive (an archive, a search or a preview) cannot take a move.
        var model = SourceModel;
        return model is not ContentModelBase { IsReadOnly: true }
            && FocusedEntry.ActionableUrls(model).Count > 0;
    }

    protected override void OnBeforeShow(MultiRenameDialog dialog)
    {
        var model = SourceModel;
        if (model is null)
        {
            return;
        }

        dialog.Prepare(model, FocusedEntry.ActionableUrls(model));
    }
}
