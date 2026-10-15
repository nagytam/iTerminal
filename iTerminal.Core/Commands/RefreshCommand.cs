using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands;

public class RefreshCommand(IContentModel contentModel) : Command
{
    public override string Title => "Refresh";
    public override string Description => "Reload the current folder content.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.R, false, false, true);

    public override Task ExecuteAsync(object? arg)
    {
        contentModel?.Refresh(true);
        return Task.CompletedTask;
    }
}
