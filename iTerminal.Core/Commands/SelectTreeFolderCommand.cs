using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;

namespace iTerminal.Core.Commands;

public class SelectTreeFolderCommand(IContentModelService contentModelService, FolderIndexModel folderIndexModel) 
    : Command
{
    public override string Title => "Select folder";
    public override string Description => "Go to the folder under the cursor in the tree.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        var canExecute = base.CanExecute();
        var folderSelectorFolderTreeTabPage = Control?.Parent as FolderSelectorFolderTreeTabPage;
        if (folderSelectorFolderTreeTabPage is not null)
        {
            var focusedItem = folderIndexModel.GetFocusedItem();
            canExecute = canExecute && (focusedItem is not null) && (focusedItem.Name != "My Computer");
        }
        return canExecute;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        var folderSelectorFolderTreeTabPage = Control?.Parent as FolderSelectorFolderTreeTabPage;
        if (folderSelectorFolderTreeTabPage is not null)
        {
            var focusedItem = folderIndexModel.GetFocusedItem();
            if (focusedItem is not null)
            {
                var contentModel = contentModelService.GetLastContentModel();
                if (contentModel is not null)
                {
                    var focusedPath = focusedItem.Name;
                    await contentModelService.SetUrlAsync(contentModel.Name, focusedPath);
                    CloseContainingDialog();
                }
            }
        }
    }
}
