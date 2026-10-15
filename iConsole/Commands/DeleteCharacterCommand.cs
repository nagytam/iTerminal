using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

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
        int cursorY = (int)scrollableModel.CursorPosition.Y;
        var text = textModel.Lines[cursorY];
        if (!string.IsNullOrEmpty(text))
        {
            // Delete the character before the cursor (backspace-like behavior).
            if (scrollableModel.CursorPosition.X > 0)
            {
                var removeIndex = (int)scrollableModel.CursorPosition.X - 1;
                text = text.Remove(removeIndex, 1);
                textModel.Lines[cursorY] = text;
                textModel.TriggerPropertyChanged();

                scrollableModel.CursorPosition.X--;
            }
        }
        return Task.CompletedTask;
    }
}
