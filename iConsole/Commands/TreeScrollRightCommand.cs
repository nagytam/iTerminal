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
            var nextItem = focusedItem.Children.AsSnapshot().First();
            if (nextItem is not null)
            {
                focusedItem.IsOpened = true;
                focusedItem.IsFocused = false;
                nextItem.IsFocused = true;
            }
        }
        return Task.CompletedTask;
    }
}
