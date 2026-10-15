using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollLeftCommand(bool isCursorEnabled, IScrollableModel scrollableModel) : Command
{
    public override string Title => "Move left";
    public override string Description => "Move the cursor or viewport left by one column.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.LeftArrow, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        if (scrollableModel.CursorPosition.X > 0)
        {
            scrollableModel.CursorPosition.X--;
        }

        if (isCursorEnabled)
        {
            if (scrollableModel.ScrollPosition.X > 0 && scrollableModel.ScrollPosition.X == scrollableModel.CursorPosition.X)
            {
                scrollableModel.ScrollPosition.X--;
            }
        }
        else
        {
            if (scrollableModel.ScrollPosition.X > 0)
            {
                scrollableModel.ScrollPosition.X--;
            }
        }
        return Task.CompletedTask;
    }
}
