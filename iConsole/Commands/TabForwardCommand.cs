using iConsole.Data;

namespace iConsole.Commands;

public class TabForwardCommand : Command
{
    public override string Title => "Next panel";
    public override string Description => "Move focus to the next focusable panel or control.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Tab, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        Control?.OnHandleTabForward();
        return Task.CompletedTask;
    }
}
