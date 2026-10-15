using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands.Marks;

/// <summary>
/// Shift+Down / Shift+Up: marks or unmarks the row under the cursor and moves on — Insert in either
/// direction, so holding Shift and running the cursor over a block of rows marks the block.
/// </summary>
public class MarkAndMoveCommand : MarkCommand
{
    private readonly int _step;

    public MarkAndMoveCommand(IContentModelService contentModelService, int step, IContentModel? ownModel = null)
        : base(contentModelService, ownModel)
    {
        _step = step;
        Key = new Key(step > 0 ? ConsoleKey.DownArrow : ConsoleKey.UpArrow, true, false, false);
    }

    public override string Title => _step > 0 ? "Mark down" : "Mark up";
    public override string Description => _step > 0
        ? "Mark or unmark the row under the cursor and move down."
        : "Mark or unmark the row under the cursor and move up.";

    public override Key? Key { get; init; }

    public override Task ExecuteAsync(object? args)
    {
        if (TargetModel is { } model && Marks is { } marks)
        {
            marks.ToggleMark(model.GetFocusedEntry());
            MoveCursor(model, _step);
            Repaint(model);
        }

        return Task.CompletedTask;
    }
}
