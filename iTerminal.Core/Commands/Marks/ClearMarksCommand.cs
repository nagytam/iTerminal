using iConsole.Interfaces;

namespace iTerminal.Core.Commands.Marks;

/// <summary>Unmarks everything in the panel.</summary>
public class ClearMarksCommand(IContentModelService contentModelService, IContentModel? ownModel = null)
    : MarkCommand(contentModelService, ownModel)
{
    public override string Title => "Unmark all";
    public override string Description => "Unmark every row in the panel.";

    public override bool CanExecute() => Marks is { MarkedCount: > 0 };

    public override Task ExecuteAsync(object? args)
    {
        if (TargetModel is { } model && Marks is { } marks)
        {
            marks.ClearMarks();
            Repaint(model);
        }

        return Task.CompletedTask;
    }
}
