using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class DeleteDialogCommand(
    DeleteDialog deleteDialog,
    IContentModelService contentModelService,
    INotificationService? notificationService = null)
    : ShowDialogCommand<DeleteDialog>(deleteDialog)
{
    public override string Title => "Delete";
    public override string Description => "Delete the marked files and folders, or the one under the cursor.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F8, false, false, false);

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (!isHandled && Key?.IsIdentical(keyEvent) == true)
        {
            FileKeyRefusal.Notify(notificationService);
        }
        return isHandled;
    }

    public override bool CanExecute()
    {
        return FocusedEntry.ActionableUrls(SourceModel).Count > 0;
    }

    /// <summary>The panel F8 acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService.GetFocusedModel() ?? contentModelService.GetLastContentModel();

    protected override void OnBeforeShow(DeleteDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        dialog.SourceModel = SourceModel;
    }
}
