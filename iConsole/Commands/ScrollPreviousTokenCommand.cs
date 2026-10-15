using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollPreviousTokenCommand(bool isCursorEnabled, IScrollableModel scrollableModel, ITextModel textModel) 
    : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.LeftArrow, false, false, true);

    public override Task ExecuteAsync(object? args)
    {
        var line = textModel.Lines[(int)scrollableModel.CursorPosition.Y];
        var remaining = line[..Math.Min(line.Length, Math.Max(0, (int)(scrollableModel.CursorPosition.X - 1)))];
        var previousIndex = remaining.LastIndexOfAny([' ']) + 1;

        if (previousIndex >= 0)
        {
            scrollableModel.CursorPosition.X = previousIndex;
        }

        if (isCursorEnabled)
        {
            if (scrollableModel.ScrollPosition.X > 0 && scrollableModel.ScrollPosition.X == scrollableModel.CursorPosition.X)
            {
                scrollableModel.ScrollPosition.X--;
            }
        }
        else
        {
            if (scrollableModel.ScrollPosition.X > 0)
            {
                scrollableModel.ScrollPosition.X--;
            }
        }
        return Task.CompletedTask;
    }
}
