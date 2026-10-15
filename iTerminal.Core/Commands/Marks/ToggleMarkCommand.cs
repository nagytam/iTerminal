using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands.Marks;

/// <summary>
/// Insert: marks or unmarks the row under the cursor and steps down, the way every two-panel file
/// manager marks files — so a run of Insert presses marks a run of rows. On the <c>..</c> row it
/// only steps down.
/// </summary>
public class ToggleMarkCommand(IContentModelService contentModelService, IContentModel? ownModel = null)
    : MarkCommand(contentModelService, ownModel)
{
    public override string Title => "Mark";
    public override string Description => "Mark or unmark the row under the cursor and step down.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Insert, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        if (TargetModel is { } model && Marks is { } marks)
        {
            marks.ToggleMark(model.GetFocusedEntry());
            MoveCursor(model, 1);
            Repaint(model);
        }

        return Task.CompletedTask;
    }
}
