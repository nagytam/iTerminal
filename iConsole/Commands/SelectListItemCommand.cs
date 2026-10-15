using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

/// <summary>
/// Selects the item currently under the cursor in a ListBox by pressing Enter.
/// Deselects all other items (single-selection behaviour).
/// </summary>
public class SelectListItemCommand<T>(IScrollableModel scrollableModel, IListBoxModel<T> listBoxModel) : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        var cursorY = (int)scrollableModel.CursorPosition.Y;
        var items = listBoxModel.ListItems.ToList();

        for (int i = 0; i < items.Count; i++)
        {
            items[i].IsSelected = (i == cursorY) && items[i].IsEnabled;
        }

        return Task.CompletedTask;
    }
}
