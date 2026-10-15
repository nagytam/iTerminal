using iConsole.Data;
using iConsole.Interfaces;

namespace iTerminal.Core.Commands.Marks;

/// <summary>
/// Num*: flips the mark of every file in the panel. Folders are left as they are, as in every
/// commander — "everything but these three files" almost never means the sub-folders too.
/// </summary>
public class InvertMarksCommand(IContentModelService contentModelService, IContentModel? ownModel = null)
    : MarkCommand(contentModelService, ownModel)
{
    public override string Title => "Invert marks";
    public override string Description => "Mark every unmarked file and unmark every marked one.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Multiply, false, false, false);

    public override Task ExecuteAsync(object? args)
    {
        if (TargetModel is { } model && Marks is { } marks)
        {
            marks.InvertMarks(includeFolders: false);
            Repaint(model);
        }

        return Task.CompletedTask;
    }
}
