using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class TreeScrollLeftCommand(IHierarchicalItemModel root) : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.LeftArrow, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        var focusedItem = root.GetFocusedItem();
        if (focusedItem != null && focusedItem.Parent != null)
        {
            focusedItem.IsOpened = false;
            focusedItem.IsFocused = false;
            focusedItem.Parent.IsOpened = false;
            focusedItem.Parent.IsFocused = true;
        }
        return Task.CompletedTask;
    }
}
