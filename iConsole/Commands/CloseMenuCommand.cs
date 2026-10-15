using iConsole.Controls;
using iConsole.Data;

namespace iConsole.Commands;

public class CloseMenuCommand : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Escape, false, false, false);

    private static MenuItem? FindOpenedMenuItem(IControl? control)
    {
        var current = control;
        while (current is not null)
        {
            if (current is MenuItem menuItem && menuItem.IsOpened)
            {
                return menuItem;
            }
            current = current.Parent;
        }
        return null;
    }

    public override bool CanExecute()
    {
        return FindOpenedMenuItem(Control) is not null;
    }

    public override Task ExecuteAsync(object? arg)
    {
        FindOpenedMenuItem(Control)?.CloseMenu();
        return Task.CompletedTask;
    }
}


