using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+L: change where the link under the cursor leads (<see cref="EditLinkDialog"/>).
/// Offered for a symbolic link and for any file on the local disk — whether that file has other
/// names, which makes it a hard link, is for the dialog to find out off the key handler.
/// </summary>
public class EditLinkDialogCommand(
    EditLinkDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<EditLinkDialog>(dialog)
{
    public override string Title => "Edit link";
    public override string Description => "Change the target of the selected symbolic or hard link.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.L, true, false, true);

    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    private Url? SourceUrl =>
        FocusedEntry.ActionableUrl(SourceModel) is { ContainerType: ContainerType.FileSystem } url ? url : null;

    public override bool CanExecute() =>
        SourceUrl is { } url && (url.IsLink || url.PathType != PathType.Folder);

    protected override void OnBeforeShow(EditLinkDialog dialog)
    {
        if (SourceUrl is { } url)
        {
            dialog.Load(url.Path);
        }
    }
}
