using iConsole.Data;

namespace iConsole.Commands;

public class CancelCommand(CancellationTokenSource cancellationTokenSource) : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Escape, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        cancellationTokenSource.Cancel();
        Control?.Parent?.Close();
        return Task.CompletedTask;
    }
}
