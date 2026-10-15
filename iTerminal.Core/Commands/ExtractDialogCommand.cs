using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Shift+F5 — Alt+F5's other half: opens <see cref="ExtractDialog"/> over the marked archives, or
/// the archive under the cursor. Offered only when there is an archive among them.
/// </summary>
public class ExtractDialogCommand(
    ExtractDialog extractDialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<ExtractDialog>(extractDialog)
{
    public override string Title => "Extract";
    public override string Description => "Unpack the marked archives, or the one under the cursor, into a folder.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F5, true, false, false);

    /// <summary>The panel Shift+F5 acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    public override bool CanExecute() =>
        ExtractDialog.ArchivesAmong(FocusedEntry.ActionableUrls(SourceModel), Dialog.ExtractionService).Count > 0;

    protected override void OnBeforeShow(ExtractDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        dialog.SourceModel = SourceModel;
    }
}
