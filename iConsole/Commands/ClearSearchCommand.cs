using iConsole.Controls;
using iConsole.Data;

namespace iConsole.Commands;

/// <summary>Clears a focused <see cref="SearchBox"/> back to an empty query.</summary>
public class ClearSearchCommand : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Escape, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        (Control as SearchBox)?.Clear();
        return Task.CompletedTask;
    }
}
