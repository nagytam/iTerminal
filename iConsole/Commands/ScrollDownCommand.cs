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
        // One line down: with several columns the cursor moves to the same column of the next row.
        var columns = ScrollableModel.ColumnCountOf(scrollableModel);
        scrollableModel.CursorPosition.Y = Math.Max(0, Math.Min(
            scrollableModel.CursorPosition.Y + columns,
            scrollableModel.ScrollableSize.Height - 1));
        Debug.Assert(scrollableModel.CursorPosition.Y <= Math.Max(0, scrollableModel.ScrollableSize.Height - 1));
        ScrollableModel.ScrollLinesToVisible(scrollableModel);

        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
