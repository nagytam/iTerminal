using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class CopyDialogCommand(
    CopyDialog copyDialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<CopyDialog>(copyDialog)
{
    public override string Title => "Copy";
    public override string Description => "Copy the marked files and folders, or the one under the cursor, to another location.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F5, false, false, false);

    public override bool CanExecute()
    {
        // Folders too: the transfer copies a folder through its folder pipeline, and F5 refusing one
        // (it used to accept files only) left a folder copy reachable through no key at all. With
        // rows marked, those are what is copied, wherever the cursor is.
        return FocusedEntry.ActionableUrls(SourceModel).Count > 0;
    }

    /// <summary>The panel F5 acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    protected override void OnBeforeShow(CopyDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        dialog.SourceModel = SourceModel;
    }
}
