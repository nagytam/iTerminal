using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollPageDownCommand(IScrollableModel scrollableModel) : Command
{
    public override string Title => "Page down";
    public override string Description => "Move the selection or viewport down by one page.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.PageDown, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        // A page is every visible entry: the view's lines times the columns of a line.
        var columns = ScrollableModel.ColumnCountOf(scrollableModel);
        var page = scrollableModel.ViewSize.Height * columns;
        // The last scroll position that still fills the view: whole lines, the last one at the bottom.
        var lines = (scrollableModel.ScrollableSize.Height + columns - 1) / columns;
        var maxScroll = Math.Max(0, lines - scrollableModel.ViewSize.Height) * columns;
        scrollableModel.CursorPosition.Y = Math.Min(
            scrollableModel.CursorPosition.Y + page,
            Math.Max(0, scrollableModel.ScrollableSize.Height - 1));
        scrollableModel.ScrollPosition.Y = Math.Min(
            scrollableModel.ScrollPosition.Y + page,
            maxScroll);
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
