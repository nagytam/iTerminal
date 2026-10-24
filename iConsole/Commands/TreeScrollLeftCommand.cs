using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class TreeScrollLeftCommand(IHierarchicalItemModel root) : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.LeftArrow, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        // An open node closes where it is; a closed one or a leaf steps up to its parent, which stays open.
        var focusedItem = root.GetFocusedItem();
        if (focusedItem != null && focusedItem.IsOpened && focusedItem.Children.Any())
        {
            focusedItem.IsOpened = false;
        }
        else if (focusedItem != null && focusedItem.Parent != null)
        {
            focusedItem.IsFocused = false;
            focusedItem.Parent.IsFocused = true;
        }
        return Task.CompletedTask;
    }
}
