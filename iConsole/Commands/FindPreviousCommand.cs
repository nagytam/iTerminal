using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class FindPreviousCommand(ISearchModel searchModel, IScrollableModel scrollableModel)
    : Command
{
    public override string Title => "Ctrl-F3-FindPrev";
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F3, false, false, true);

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
            Position previousPosition;
            if (searchModel.Hits.Any(h =>
                 (h.Y == cursorPosition.Y && h.X < cursorPosition.X) ||
                 (h.Y < cursorPosition.Y)))
            {
                previousPosition = searchModel.Hits.Last(h =>
                 (h.Y == cursorPosition.Y && h.X < cursorPosition.X) ||
                 (h.Y < cursorPosition.Y));
            }
            else
            {
                previousPosition = searchModel.Hits.Last();
            }
            scrollableModel.CursorPosition.Y = previousPosition.Y;
            scrollableModel.CursorPosition.X = previousPosition.X;
            scrollableModel.ScrollToVisible();
        }
        return Task.CompletedTask;
    }
}
