using iConsole.Controls;
using iConsole.Data;

namespace iConsole.Commands;

public class ToggleCheckBoxCommand : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Spacebar, false, false, false);

    public override bool CanExecute()
    {
        if (IsReadOnlyControl)
        {
            return false;
        }
        return base.CanExecute();
    }

    public override Task ExecuteAsync(object? args)
    {
        var checkBox = Control as CheckBox;
        if (checkBox is not null)
        {
            checkBox.IsChecked = !checkBox.IsChecked;
        }
        return Task.CompletedTask;
    }
}
