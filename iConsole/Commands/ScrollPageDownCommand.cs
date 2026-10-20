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
        scrollableModel.CursorPosition.Y = (uint)Math.Min(
            scrollableModel.CursorPosition.Y + scrollableModel.ViewSize.Height,
            Math.Max(0, (int)scrollableModel.ScrollableSize.Height - 1));
        scrollableModel.ScrollPosition.Y = (uint)Math.Min(
            scrollableModel.ScrollPosition.Y + scrollableModel.ViewSize.Height,
            Math.Max(0,
            (int)scrollableModel.ScrollableSize.Height - scrollableModel.ViewSize.Height));
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
