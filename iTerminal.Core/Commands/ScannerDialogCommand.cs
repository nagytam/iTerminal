using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs.Scanning;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+I: the scan dialog (<see cref="ScannerDialog"/>). It opens on the folder the focused panel shows, which is
/// where <c>W</c> saves. A remote or archive folder has no local path to save to, and the dialog says so.
/// </summary>
public class ScannerDialogCommand(
    ScannerDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<ScannerDialog>(dialog)
{
    public override string Title => "Scan";
    public override string Description => "Scan a page or the document feeder, preview it and save it.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.I, true, false, true);

    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    /// <summary>The local folder the panel shows, or null for a remote or archive folder.</summary>
    private string? FolderPath =>
        SourceModel?.Url is { PathType: PathType.Folder, ContainerType: ContainerType.FileSystem } url ? url.FullName : null;

    public override bool CanExecute() => true;

    protected override void OnBeforeShow(ScannerDialog dialog) => dialog.Load(FolderPath);
}
