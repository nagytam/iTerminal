using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class TreeScrollDownCommand(IHierarchicalItemModel root) : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.DownArrow, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        var focusedItem = root.GetFocusedItem();
        if (focusedItem != null)
        {
            var nextItem = focusedItem.GetNextItem();
            if (nextItem is not null)
            {
                focusedItem.IsFocused = false;
                nextItem.IsFocused = true;
            }
        }
        return Task.CompletedTask;
    }
}
