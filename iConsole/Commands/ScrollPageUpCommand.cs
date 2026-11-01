using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollPageUpCommand(IScrollableModel scrollableModel) : Command
{
    public override string Title => "Page up";
    public override string Description => "Move the selection or viewport up by one page.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.PageUp, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        // A page is every visible entry: the view's lines times the columns of a line.
        var page = scrollableModel.ViewSize.Height * ScrollableModel.ColumnCountOf(scrollableModel);
        scrollableModel.CursorPosition.Y = Math.Max(scrollableModel.CursorPosition.Y - page, 0);
        scrollableModel.ScrollPosition.Y = Math.Max(scrollableModel.ScrollPosition.Y - page, 0);
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
