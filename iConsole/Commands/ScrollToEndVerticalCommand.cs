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
        scrollableModel.CursorPosition.Y = Math.Max(0, scrollableModel.ScrollableSize.Height - 1);
        ScrollableModel.ScrollLinesToVisible(scrollableModel);
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
