using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class ScrollToEndHorizontalCommand(IScrollableModel scrollableModel, ITextModel textModel) : Command
{
    public override string Title => "Line end";
    public override string Description => "Jump to the end of the current line.";

    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.End, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        var line = textModel.Lines[(int)scrollableModel.ScrollPosition.Y];
        scrollableModel.CursorPosition.X = line.Length;
        scrollableModel.ScrollPosition.X = Math.Max(0,
        scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width);
        return Task.CompletedTask;
    }
}
