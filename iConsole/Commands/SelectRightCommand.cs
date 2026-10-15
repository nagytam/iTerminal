using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class SelectRightCommand(bool isCursorEnabled, IScrollableModel scrollableModel, ISelectionTextModel selectionTextModel) 
    : Command
{
    private readonly IScrollableModel _scrollableModel = scrollableModel;
    private readonly ISelectionTextModel _selectionTextModel = selectionTextModel;
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.RightArrow, true, false, false);

    public override Task ExecuteAsync(object? args)
    {
        var cursorPositionX = _scrollableModel.CursorPosition.X;
        var cursorPositionY = _scrollableModel.CursorPosition.Y;

        if (isCursorEnabled)
        {
            if (scrollableModel.CursorPosition.X < scrollableModel.ScrollableSize.Width)
            {
                scrollableModel.CursorPosition.X++;
            }
            if (scrollableModel.ScrollPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width &&
                scrollableModel.CursorPosition.X == scrollableModel.ScrollPosition.X + scrollableModel.ScrollableSize.Width - 1)
            {
                scrollableModel.ScrollPosition.X++;
            }
        }
        else
        {
            if (scrollableModel.CursorPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width - 1)
            {
                scrollableModel.CursorPosition.X++;
            }
            if (scrollableModel.ScrollPosition.X < scrollableModel.ScrollableSize.Width - scrollableModel.ViewSize.Width)
            {
                scrollableModel.ScrollPosition.X++;
            }
        }
        if (!_selectionTextModel.IsSelected)
        {
            _selectionTextModel.IsSelected = true;
            _selectionTextModel.Start = new(cursorPositionX, cursorPositionY);
            _selectionTextModel.End = new(cursorPositionX, cursorPositionY);
        }
        else
        {
            _selectionTextModel.Start = new(cursorPositionX, cursorPositionY);
            _selectionTextModel.End = new(cursorPositionX, cursorPositionY);
        }
        return Task.CompletedTask;
    }
}
