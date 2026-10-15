using iConsole.Commands;
using iConsole.Data;
using iTerminal.Core.Controls.Contents;

namespace iTerminal.Core.Commands;

public class CycleBottomBarModeCommand(Content? content) : Command
{
    public override string Title => "AI bar / terminal bar";
    public override string Description => "Switch the bar under the panels between the AI prompt and the terminal command line.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.B, false, false, true);

    public override Task ExecuteAsync(object? arg)
    {
        content?.CycleBottomBarMode();
        return Task.CompletedTask;
    }
}
