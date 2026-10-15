using iConsole.Data;

namespace iConsole.Commands;

public class TabBackwardCommand : Command
{
    public override string Title => "Previous panel";
    public override string Description => "Move focus to the previous focusable panel or control.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Tab, true, false, false);

    public override Task ExecuteAsync(object? args)
    {
        Control?.OnHandleTabBackward();
        return Task.CompletedTask;
    }
}
