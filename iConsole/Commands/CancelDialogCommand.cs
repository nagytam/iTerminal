using iConsole.Data;

namespace iConsole.Commands;

public class CancelDialogCommand : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Escape, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        Control?.Parent?.Close();
        return Task.CompletedTask;
    }
}
