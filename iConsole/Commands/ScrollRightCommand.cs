using iConsole.Data;
using iConsole.Model;
using System.Diagnostics;

namespace iConsole.Commands;

public class ScrollRightCommand(bool isCursorEnabled, IScrollableModel scrollableModel) : Command
{
    public override string Title => "Move right";
    public override string Description => "Move the cursor or viewport right by one column.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.RightArrow, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        Debug.Assert(!scrollableModel.ViewSize.IsEmpty);

        if (isCursorEnabled)
        {
            if (scrollableModel.CursorPosition.X < Math.Min(scrollableModel.ScrollableSize.Width, scrollableModel.ScrollPosition.X + scrollableModel.ViewSize.Width))
            {
                scrollableModel.CursorPosition.X++;
            }
            if (scrollableModel.ScrollPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width &&
                scrollableModel.CursorPosition.X == scrollableModel.ScrollPosition.X + scrollableModel.ScrollableSize.Width - 1)
            {
                scrollableModel.ScrollPosition.X++;
            }
        }
        else
        {
            if (scrollableModel.CursorPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width - 1)
            {
                scrollableModel.CursorPosition.X++;
            }
            if (scrollableModel.ScrollPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width)
            {
                scrollableModel.ScrollPosition.X++;
            }
        }
        return Task.CompletedTask;
    }
}
