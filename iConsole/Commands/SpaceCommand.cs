using iConsole.Controls.Buttons;
using iConsole.Data;

namespace iConsole.Commands;

public class SpaceCommand : Command
{
    public override string Title => "Press or tick";

    public override string Description => "Press the focused button, or tick or untick the focused check box.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Spacebar, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        var button = Control as Button;
        button?.Commands.First().RunAsync(args);
        return Task.CompletedTask;
    }
}
