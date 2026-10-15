using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

public class TaskManagerDialogCommand(TaskManagerDialog taskManagerDialog) : ShowDialogCommand<TaskManagerDialog>(taskManagerDialog)
{
    public override string Title => "Task manager";
    public override string Description => "Show background tasks and their progress.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F9, false, false, false);
}
