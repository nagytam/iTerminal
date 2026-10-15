using iConsole.Data;

namespace iConsole.Commands;

public class CloseParentCommand : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        Control?.Parent?.Close();
        return Task.CompletedTask;
    }
}
