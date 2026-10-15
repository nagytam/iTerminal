using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class InvertSelectionCommand<T>(IScrollableModel scrollableModel, IListBoxModel<T> listBoxModel) : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.Insert, false, false, false);

    public override Task ExecuteAsync(object? arg)
    {
        var cursorPositionY = (int)scrollableModel.CursorPosition.Y;
        var listBoxItems = listBoxModel.ListItems.ToList();
        if (cursorPositionY < listBoxItems.Count)
        {
            var isSelected = listBoxItems[cursorPositionY].IsSelected;
            listBoxItems[cursorPositionY].IsSelected = !isSelected;
        }
        return Task.CompletedTask;
    }
}
