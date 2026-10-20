using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

/// <summary>
/// Clears the line the cursor is on and puts the cursor at its start. The line itself stays, so the lines below do
/// not move. Does nothing on an empty document.
/// </summary>
public class DeleteLineCommand(ITextModel textModel, IScrollableModel scrollableModel)
    : Command
{
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.K, false, false, true);

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
        if (textModel.Lines.Count == 0)
        {
            return Task.CompletedTask;
        }

        var row = (int)Math.Clamp(scrollableModel.CursorPosition.Y, 0, textModel.Lines.Count - 1);
        if (textModel.Lines[row].Length > 0 && Control is IDirtyModel dirtyModel)
        {
            dirtyModel.IsDirty = true;
        }

        textModel.Lines[row] = string.Empty;
        textModel.TriggerPropertyChanged();
        scrollableModel.CursorPosition.Y = row;
        scrollableModel.CursorPosition.X = 0;
        scrollableModel.ScrollPosition.X = 0;
        Control?.Refresh();
        return Task.CompletedTask;
    }
}
