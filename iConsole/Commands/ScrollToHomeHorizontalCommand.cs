using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollToHomeHorizontalCommand(IScrollableModel scrollableModel) : Command
{
    public override string Title => "Line start";
    public override string Description => "Jump to the start of the current line.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.Home, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        scrollableModel.CursorPosition.X = 0;
        scrollableModel.ScrollPosition.X = 0;
        return Task.CompletedTask;
    }
}
