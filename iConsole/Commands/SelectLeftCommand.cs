using iConsole.Data;
using iConsole.Model;

namespace iConsole.Commands;

public class SelectLeftCommand(bool isCursorEnabled, IScrollableModel scrollableModel, ISelectionTextModel selectionTextModel) : Command
{
    private readonly IScrollableModel _scrollableModel = scrollableModel;
    private readonly ISelectionTextModel _selectionTextModel = selectionTextModel;
    public override Key? Key { get; init; } = 
        new Key(ConsoleKey.LeftArrow, true, false, false);

    public override Task ExecuteAsync(object? args)
    {
        var cursorPositionX = _scrollableModel.CursorPosition.X;
        var cursorPositionY = _scrollableModel.CursorPosition.Y;

        if (scrollableModel.CursorPosition.X > 0)
        {
            scrollableModel.CursorPosition.X--;
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
