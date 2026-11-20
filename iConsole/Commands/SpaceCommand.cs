using iConsole.Controls.Buttons;
using iConsole.Data;

namespace iConsole.Commands;

public class SpaceCommand : Command
{
    public override string Title => "Press or tick";

    public override string Description => "Press the focused button, or tick or untick the focused check box.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Spacebar, false, false, false);

    public override async Task ExecuteAsync(object? args)
    {
        if (Control is not Button button)
        {
            return;
        }
        button.StartPress();
        if (!await button.Commands.First().RunAsync(args))
        {
            button.StopPress();
        }
    }
}
