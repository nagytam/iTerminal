using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Commands;

public class SelectHistoryFolderCommand(
    IContentModelService contentModelService,
    FolderHistoryService folderHistoryService) : Command
{
    public override string Title => "Select folder";
    public override string Description => "Go back to the visited folder under the cursor.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        return base.CanExecute() && GetFocusedFolder() is not null;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        var focusedItem = GetFocusedFolder();
        if (focusedItem is null)
        {
            return;
        }
        var contentModel = contentModelService.GetLastContentModel();
        if (contentModel is not null)
        {
            await contentModelService.SetUrlAsync(contentModel.Name, focusedItem);
        }
        CloseContainingDialog();
    }

    /// <summary>
    /// The history entry under the cursor, or null when the page has no rows or the cursor is off
    /// the end of them. The list and the history are two views of one collection but they are not
    /// updated in the same breath — the list rebuilds on idle — so the index has to be checked
    /// rather than assumed.
    /// </summary>
    private string? GetFocusedFolder()
    {
        if (Control?.Parent is not FolderSelectorHistoryTabPage historyTabPage)
        {
            return null;
        }
        var history = folderHistoryService?.History.AsSnapshot();
        if (history is null)
        {
            return null;
        }
        var index = (int)historyTabPage.ScrollableModel.CursorPosition.Y;
        return (index >= 0 && index < history.Count) ? history[index] : null;
    }
}
