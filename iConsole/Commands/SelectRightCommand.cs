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
            if (_scrollableModel.CursorPosition.X < _scrollableModel.ScrollableSize.Width)
            {
                _scrollableModel.CursorPosition.X++;
            }
            if (_scrollableModel.ScrollPosition.X < _scrollableModel.ScrollableSize.Width - _scrollableModel.ViewSize.Width &&
                _scrollableModel.CursorPosition.X == _scrollableModel.ScrollPosition.X + _scrollableModel.ScrollableSize.Width - 1)
            {
                _scrollableModel.ScrollPosition.X++;
            }
        }
        else
        {
            if (_scrollableModel.CursorPosition.X < _scrollableModel.ScrollableSize.Width - _scrollableModel.ViewSize.Width - 1)
            {
                _scrollableModel.CursorPosition.X++;
            }
            if (_scrollableModel.ScrollPosition.X < _scrollableModel.ScrollableSize.Width - _scrollableModel.ViewSize.Width)
            {
                _scrollableModel.ScrollPosition.X++;
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
