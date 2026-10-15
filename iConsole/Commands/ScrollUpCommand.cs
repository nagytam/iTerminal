using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollUpCommand(IScrollableModel scrollableModel) : Command
{
    public override string Title => "Move up";
    public override string Description => "Move the selection or viewport up by one line.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.UpArrow, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        if (scrollableModel.CursorPosition.Y == scrollableModel.ScrollPosition.Y)
        {
            if (scrollableModel.ScrollPosition.Y > 0)
            {
                scrollableModel.ScrollPosition.Y--;
            }            
        }
        if (scrollableModel.CursorPosition.Y > 0)
        {
            scrollableModel.CursorPosition.Y--;
        }
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
