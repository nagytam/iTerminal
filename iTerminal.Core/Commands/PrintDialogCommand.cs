using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+P: print the file under the cursor and manage the print queue (<see cref="PrintDialog"/>). Unlike the
/// other file dialogs it always runs: with no printable file under the cursor (the <c>..</c> row, a folder,
/// an archive row, a remote drive) it opens as the queue manager alone. Local files only: the tools are handed a path.
/// </summary>
public class PrintDialogCommand(
    PrintDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<PrintDialog>(dialog)
{
    public override string Title => "Print";
    public override string Description => "Print the file under the cursor and manage the print queue: hold, resume and cancel jobs.";

    // Key ctor is (consoleKey, shift, alt, control). Ctrl+Shift+P is Windows Terminal's command palette.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.P, false, false, true);

    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    private Url? SourceUrl =>
        FocusedEntry.ActionableUrl(SourceModel) is { PathType: PathType.File, ContainerType: ContainerType.FileSystem } url ? url : null;

    /// <summary>Whether a local file is under the cursor: what the right-click menu needs to decide to offer "Print".</summary>
    public bool HasPrintableFile => SourceUrl is not null;

    public override bool CanExecute() => true;

    protected override void OnBeforeShow(PrintDialog dialog) => dialog.Load(SourceUrl?.FullName);
}
