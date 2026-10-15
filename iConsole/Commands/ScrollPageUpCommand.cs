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
        scrollableModel.CursorPosition.Y = (uint)Math.Max(
            scrollableModel.CursorPosition.Y - (int)scrollableModel.ViewSize.Height,
            0);
        scrollableModel.ScrollPosition.Y = (uint)Math.Max(
            scrollableModel.ScrollPosition.Y - (int)scrollableModel.ViewSize.Height,
            0);
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
