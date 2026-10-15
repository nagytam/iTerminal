using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;

namespace iTerminal.Core.Commands;

public class DeleteFavouriteCommand(
    IContentModelService contentModelService, 
    IScrollableModel scrollableModel,
    ConcurrentObservableCollection<Url> favourites) : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        return favourites.Any();
    }

    public override Task ExecuteAsync(object? arg)
    {
        var contentModel = contentModelService.GetLastContentModel();
        if (contentModel is not null)
        {
            var focusedIndex = (int)scrollableModel.CursorPosition.Y;
            favourites.RemoveAt(focusedIndex);
        }
        return Task.CompletedTask;
    }
}
