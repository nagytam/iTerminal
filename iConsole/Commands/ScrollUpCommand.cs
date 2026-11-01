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
        // One line up: with several columns the cursor moves to the same column of the previous row.
        var columns = ScrollableModel.ColumnCountOf(scrollableModel);
        scrollableModel.CursorPosition.Y = Math.Max(0, scrollableModel.CursorPosition.Y - columns);
        ScrollableModel.ScrollLinesToVisible(scrollableModel);
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
