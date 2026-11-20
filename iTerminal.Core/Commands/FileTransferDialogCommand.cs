using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs.FileTransfer;

namespace iTerminal.Core.Commands;

/// <summary>
/// Tools > File transfer: opens <see cref="FileTransferDialog"/> over the entries the focused panel would act on, and
/// starts the share. The selection is taken now, while the panel still has focus.
/// </summary>
public class FileTransferDialogCommand(FileTransferDialog fileTransferDialog, IContentModelService contentModelService)
    : ShowDialogCommand<FileTransferDialog>(fileTransferDialog)
{
    public override string Title => "File transfer";
    public override string Description => "Share the selected files on this network: a QR code and a link open them in a phone's browser while this dialog is open.";

    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    public override bool CanExecute() => FocusedEntry.ActionableUrls(SourceModel).Count > 0;

    protected override void OnBeforeShow(FileTransferDialog dialog)
    {
        dialog.Begin(FocusedEntry.ActionableUrls(SourceModel));
    }
}
