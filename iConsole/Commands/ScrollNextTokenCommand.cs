using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollNextTokenCommand(bool isCursorEnabled, IScrollableModel scrollableModel, ITextModel textModel) : Command
{
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.RightArrow, false, false, true);

    public override Task ExecuteAsync(object? args)
    {
        var line = textModel.Lines[(int)scrollableModel.CursorPosition.Y];
        var remaining = line[Math.Min(line.Length, (int)scrollableModel.CursorPosition.X)..];
        var index = remaining.IndexOfAny([' ']);
        int nextIndex;
        if (index == -1)
        {
            nextIndex = line.Length;
        }
        else
        {
            nextIndex = (int)(index + scrollableModel.CursorPosition.X);
        }

        if (isCursorEnabled)
        {
            if (nextIndex > 0)
            {
                scrollableModel.CursorPosition.X = nextIndex + 1;
            }
            if (scrollableModel.ScrollPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width &&
                scrollableModel.CursorPosition.X == scrollableModel.ScrollPosition.X + scrollableModel.ScrollableSize.Width - 1)
            {
                scrollableModel.ScrollToVisible();
            }
        }
        else
        {
            if (nextIndex > 0)
            {
                scrollableModel.CursorPosition.X = nextIndex + 1;
            }
            if (scrollableModel.ScrollPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width)
            {
                scrollableModel.ScrollToVisible();
            }
        }
        return Task.CompletedTask;
    }
}
