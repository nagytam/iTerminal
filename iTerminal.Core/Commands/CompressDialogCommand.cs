using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Alt+F5 — the key every two-panel file manager packs files with: opens <see cref="CompressDialog"/>
/// over the marked rows, or the one under the cursor.
/// </summary>
public class CompressDialogCommand(
    CompressDialog compressDialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<CompressDialog>(compressDialog)
{
    public override string Title => "Compress";
    public override string Description => "Pack the marked files and folders, or the one under the cursor, into a new or existing archive.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F5, false, true, false);

    /// <summary>The panel Alt+F5 acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    public override bool CanExecute() => FocusedEntry.ActionableUrls(SourceModel).Count > 0;

    protected override void OnBeforeShow(CompressDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        dialog.SourceModel = SourceModel;
    }
}
