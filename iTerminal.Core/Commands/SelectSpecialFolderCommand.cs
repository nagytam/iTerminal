using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class SelectSpecialFolderCommand(IContentModelService contentModelService, ObservableCollection<string> specialFolders) : Command
{
    private readonly IContentModelService _contentModelService = contentModelService;
    private readonly ObservableCollection<string> _specialFolders = specialFolders;

    public override string Title => "Select folder";
    public override string Description => "Go to the special folder or virtual drive under the cursor.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        return base.CanExecute() && GetFocusedPlace() is not null;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        var focusedItem = GetFocusedPlace();
        if (focusedItem is null)
        {
            return;
        }
        var contentModel = _contentModelService.GetLastContentModel();
        if (contentModel is not null)
        {
            await _contentModelService.SetUrlAsync(contentModel.Name, focusedItem);
            CloseContainingDialog();
        }
    }

    private string? GetFocusedPlace()
    {
        if (Control?.Parent is not FolderSelectorSpecialFoldersTabPage specialFoldersTabPage)
        {
            return null;
        }
        var index = (int)specialFoldersTabPage.ScrollableModel.CursorPosition.Y;
        return (index >= 0 && index < _specialFolders.Count) ? _specialFolders[index] : null;
    }
}
