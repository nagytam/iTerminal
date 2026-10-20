using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

/// <summary>
/// Delete in a text editor: removes the character under the cursor, or — at the end of a line — pulls the next line
/// up onto it. The cursor stays where it is. Nothing happens at the very end of the document.
/// </summary>
public class DeleteCharacterCommand(ITextModel textModel, IScrollableModel scrollableModel)
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Delete, false, false, false);

    public override bool CanExecute()
    {
        if (IsReadOnlyControl)
        {
            return false;
        }
        return base.CanExecute();
    }

    public override Task ExecuteAsync(object? arg)
    {
        var lines = textModel.Lines;
        if (lines.Count == 0)
        {
            return Task.CompletedTask;
        }

        var row = (int)Math.Clamp(scrollableModel.CursorPosition.Y, 0, lines.Count - 1);
        var text = lines[row];
        var column = (int)Math.Min(scrollableModel.CursorPosition.X, text.Length);
        var changed = false;
        if (column < text.Length)
        {
            lines[row] = text.Remove(column, 1);
            changed = true;
        }
        else if (row + 1 < lines.Count)
        {
            lines[row] = text + lines[row + 1];
            lines.RemoveAt(row + 1);
            changed = true;
        }

        scrollableModel.CursorPosition.Y = row;
        scrollableModel.CursorPosition.X = column;
        if (changed)
        {
            if (Control is IDirtyModel dirtyModel)
            {
                dirtyModel.IsDirty = true;
            }

            textModel.TriggerPropertyChanged();
            Control?.Refresh();
        }

        return Task.CompletedTask;
    }
}
