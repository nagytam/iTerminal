using iConsole.Data;

namespace iConsole.Commands;

public class HideCommand : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Escape, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        Control?.Parent?.Hide();
        return Task.CompletedTask;
    }
}
