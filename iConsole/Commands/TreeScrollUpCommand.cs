using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class TreeScrollUpCommand(IHierarchicalItemModel root) : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.UpArrow, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        var focusedItem = root.GetFocusedItem();
        if (focusedItem is not null)
        {
            var previousItem = focusedItem.GetPreviousItem();
            if (previousItem is not null)
            {
                focusedItem.IsFocused = false;
                previousItem.IsFocused = true;
            }
        }
        return Task.CompletedTask;
    }
}
