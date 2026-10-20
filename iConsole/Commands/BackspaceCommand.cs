using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

/// <summary>
/// Backspace in a text editor: removes the character before the cursor, or — at the start of a line — joins the line
/// onto the one above and puts the cursor at the join. Nothing happens at the very start of the document.
/// </summary>
public class BackspaceCommand(ITextModel textModel, IScrollableModel scrollableModel)
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Backspace, false, false, false);

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
        if (column > 0)
        {
            lines[row] = text.Remove(column - 1, 1);
            scrollableModel.CursorPosition.Y = row;
            scrollableModel.CursorPosition.X = column - 1;
            changed = true;
        }
        else if (row > 0)
        {
            var above = lines[row - 1];
            lines[row - 1] = above + text;
            lines.RemoveAt(row);
            scrollableModel.CursorPosition.Y = row - 1;
            scrollableModel.CursorPosition.X = above.Length;
            changed = true;
        }

        if (changed)
        {
            if (Control is IDirtyModel dirtyModel)
            {
                dirtyModel.IsDirty = true;
            }

            textModel.TriggerPropertyChanged();
            Control?.Refresh();
            scrollableModel.ScrollToVisible();
        }

        return Task.CompletedTask;
    }
}
