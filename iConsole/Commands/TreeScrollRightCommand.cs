using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class TreeScrollRightCommand(IHierarchicalItemModel root) : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.RightArrow, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        var focusedItem = root.GetFocusedItem();
        if (focusedItem != null && focusedItem.Children.Any())
        {
            // A closed node opens where it is; an open one steps into its first child.
            if (!focusedItem.IsOpened)
            {
                focusedItem.IsOpened = true;
                return Task.CompletedTask;
            }

            var nextItem = focusedItem.Children.AsSnapshot().First();
            if (nextItem is not null)
            {
                focusedItem.IsFocused = false;
                nextItem.IsFocused = true;
            }
        }
        return Task.CompletedTask;
    }
}
