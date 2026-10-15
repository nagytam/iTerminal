using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class SelectFavouriteCommand(
    IContentModelService contentModelService,
    IScrollableModel scrollableModel,
    ConcurrentObservableCollection<Url> favourites) : Command
{
    public override string Title => "Select favourite";
    public override string Description => "Go to the favourite folder under the cursor.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        return base.CanExecute() && GetFocusedFavourite() is not null;
    }

    public override async Task ExecuteAsync(object? arg)
    {
        var url = GetFocusedFavourite();
        if (url is null)
        {
            return;
        }
        await contentModelService.SetUrlAsync(url);
        CloseContainingDialog();
    }

    private Url? GetFocusedFavourite()
    {
        if (Control?.Parent is not FolderSelectorFavouritesTabPage)
        {
            return null;
        }
        var listedFavourites = favourites?.AsSnapshot();
        if (listedFavourites is null)
        {
            return null;
        }
        var index = (int)scrollableModel.CursorPosition.Y;
        return (index >= 0 && index < listedFavourites.Count) ? listedFavourites[index] : null;
    }
}
