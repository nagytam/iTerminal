using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

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
        int cursorY = (int)scrollableModel.CursorPosition.Y;
        textModel.Lines[cursorY] = string.Empty;
        textModel.TriggerPropertyChanged();
        scrollableModel.CursorPosition.X = 0;
        scrollableModel.ScrollPosition.X = 0;
        return Task.CompletedTask;
    }
}
