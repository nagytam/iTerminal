using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollToEndVerticalCommand(IScrollableModel scrollableModel) : Command
{
    public override string Title => "Bottom";
    public override string Description => "Jump to the last line.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.End, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        scrollableModel.ScrollPosition.Y = Math.Max(0, (int)scrollableModel.ScrollableSize.Height - (int)scrollableModel.ViewSize.Height);
        scrollableModel.CursorPosition.Y = (int)scrollableModel.ScrollableSize.Height - 1;
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
