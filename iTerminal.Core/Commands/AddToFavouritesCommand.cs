using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands;

public class AddToFavouritesCommand(
    IContentModelService contentModelService, 
    ConcurrentObservableCollection<Url> favourites) : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        var contentModel = contentModelService.GetLastContentModel();
        if (contentModel is not null)
        {
            var url = contentModel.Url;
            if (!favourites.Any(u => u.FullUrl == url.FullUrl))
            {
                return true;
            }
        }
        return false;
    }

    public override Task ExecuteAsync(object? arg)
    {
        var contentModel = contentModelService.GetLastContentModel();
        if (contentModel is not null)
        {
            var url = contentModel.Url;
            if (!favourites.Any(u => u.FullUrl == url.FullUrl)) 
            { 
                favourites.Add(new Url(url.FullUrl));
            }
        }
        return Task.CompletedTask;
    }
}
