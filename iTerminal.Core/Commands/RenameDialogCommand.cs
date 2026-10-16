using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class RenameDialogCommand(
    RenameDialog renameDialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<RenameDialog>(renameDialog)
{
    public override string Title => "Rename";
    public override string Description => "Rename the selected file or folder.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F6, false, false, false);

    public override bool CanExecute()
    {
        return FocusedEntry.ActionableUrl(contentModelService.GetLastContentModel()) is not null;
    }

    protected override void OnBeforeShow(RenameDialog dialog)
    {
        var model = contentModelService.GetFocusedModel()!;
        var url = model.GetFocusedEntry()!.Url;
        dialog.BaseUrl = model.Url;
        dialog.SourceUrl = url;

        // Just the name: that is what is being changed, and it is where the cursor should be.
        dialog.NewName = url.Name;
    }
}
