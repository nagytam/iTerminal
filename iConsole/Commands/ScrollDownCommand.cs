using iConsole.Data;
using iConsole.Model;
using System.Diagnostics;

namespace iConsole.Commands;

public class ScrollDownCommand(IScrollableModel scrollableModel) : Command
{
    public override string Title => "Move down";
    public override string Description => "Move the selection or viewport down by one line.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.DownArrow, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        Debug.Assert(!scrollableModel.ViewSize.IsEmpty);
        if (scrollableModel.CursorPosition.Y == scrollableModel.ScrollPosition.Y + scrollableModel.ViewSize.Height - 1)
        {
            // Not past the point where the last line is at the bottom of the view.
            if (scrollableModel.ScrollPosition.Y < scrollableModel.ScrollableSize.Height - scrollableModel.ViewSize.Height)
            {
                scrollableModel.ScrollPosition.Y++;
            }
        }
        if (scrollableModel.CursorPosition.Y < scrollableModel.ScrollableSize.Height - 1)
        {
            scrollableModel.CursorPosition.Y++;
        }
        scrollableModel.CursorPosition.Y = Math.Max(0, Math.Min(
            scrollableModel.CursorPosition.Y, 
            scrollableModel.ScrollableSize.Height - 1));

        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
