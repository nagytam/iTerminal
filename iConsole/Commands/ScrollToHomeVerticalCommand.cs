using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollToHomeVerticalCommand(IScrollableModel scrollableModel) : Command
{
    public override string Title => "Top";
    public override string Description => "Jump to the first line.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.Home, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        scrollableModel.ScrollPosition.Y = 0;
        scrollableModel.CursorPosition.Y = 0;
        scrollableModel.Update();
        return Task.CompletedTask;
    }
}
