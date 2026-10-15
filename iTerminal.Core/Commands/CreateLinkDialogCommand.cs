using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+L: a link to the file or folder under the cursor (<see cref="CreateLinkDialog"/>). Offered
/// on the local disk only — the other drives have no links to make — and never on the ".." row.
/// </summary>
public class CreateLinkDialogCommand(
    CreateLinkDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<CreateLinkDialog>(dialog)
{
    public override string Title => "Create link";
    public override string Description => "Make a symbolic or hard link to the selected file or folder.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.L, false, false, true);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    private Url? SourceUrl =>
        FocusedEntry.ActionableUrl(SourceModel) is { ContainerType: ContainerType.FileSystem } url ? url : null;

    public override bool CanExecute() => SourceUrl is not null;

    protected override void OnBeforeShow(CreateLinkDialog dialog)
    {
        var url = SourceUrl;
        if (url is null)
        {
            return;
        }

        var targetPath = Path.TrimEndingDirectorySeparator(url.Path);
        var other = contentModelService.NonFocusedFolderUrl;
        var otherFolder = other is { ContainerType: ContainerType.FileSystem } ? other.Path : null;
        dialog.Load(targetPath, url.PathType == PathType.Folder, CreateLinkDialog.DefaultLinkPath(targetPath, otherFolder));
    }
}
