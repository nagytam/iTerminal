using iConsole.Interfaces;

namespace iTerminal.Core.Commands.Marks;

/// <summary>Marks every file and folder in the panel. Menu only: the keys around it are all taken.</summary>
public class MarkAllCommand(IContentModelService contentModelService, IContentModel? ownModel = null)
    : MarkCommand(contentModelService, ownModel)
{
    public override string Title => "Mark all";
    public override string Description => "Mark every file and folder in the panel.";

    public override Task ExecuteAsync(object? args)
    {
        if (TargetModel is { } model && Marks is { } marks)
        {
            marks.SetMarks("*", marked: true, includeFolders: true);
            Repaint(model);
        }

        return Task.CompletedTask;
    }
}
