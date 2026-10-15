using iConsole.Controls.Buttons;
using iConsole.Data;

namespace iConsole.Commands;

public class SpaceCommand : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Spacebar, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        var button = Control as Button;
        button?.Commands.First().RunAsync(args);
        return Task.CompletedTask;
    }
}
