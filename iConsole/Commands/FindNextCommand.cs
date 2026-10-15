using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class FindNextCommand(ISearchModel searchModel, IScrollableModel scrollableModel)
    : Command
{
    public override string Title => "F3-FindNext";
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F3, false, false, false);

    public override bool CanExecute()
    {
        var canExecute = base.CanExecute();
        canExecute = canExecute && searchModel != null && searchModel.Hits.Any();
        return canExecute;
    }

    public override Task ExecuteAsync(object? arg)
    {
        var cursorPosition = scrollableModel.CursorPosition;
        if (searchModel != null && searchModel.Hits.Any())
        {
            Position nextPosition;
            if (searchModel.Hits.Any(h =>
                 (h.Y == cursorPosition.Y && h.X > cursorPosition.X) ||
                 (h.Y > cursorPosition.Y)))
            {
                nextPosition = searchModel.Hits.First(h =>
                 (h.Y == cursorPosition.Y && h.X > cursorPosition.X) ||
                 (h.Y > cursorPosition.Y));
            }
            else
            {
                nextPosition = searchModel.Hits.First();
            }
            scrollableModel.CursorPosition.Y = nextPosition.Y;
            scrollableModel.CursorPosition.X = nextPosition.X;
            scrollableModel.ScrollToVisible();
        }
        return Task.CompletedTask;
    }
}
