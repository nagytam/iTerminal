using iConsole.Data;

namespace iConsole.Commands;

public class CloseCommand : Command
{
    public override string Title => iConsole.Localization.UiText.Get("Common.Close", "Close");
    public override string Description => "Close the current dialog or popup.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Escape, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        Control?.Close();
        return Task.CompletedTask;
    }
}
