using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

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
        int cursorY = (int)scrollableModel.CursorPosition.Y;
        var text = textModel.Lines[cursorY];
        if (!string.IsNullOrEmpty(text))
        {
            if (scrollableModel.CursorPosition.X > 0)
            {
                text = text.Remove((int)scrollableModel.CursorPosition.X - 1);
                textModel.Lines[cursorY] = text;
                textModel.TriggerPropertyChanged();

                scrollableModel.CursorPosition.X--;

                if (scrollableModel.ScrollPosition.X > 0)
                {
                    scrollableModel.ScrollPosition.X--;
                }
            }
        }
        return Task.CompletedTask;
    }
}
