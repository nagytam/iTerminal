using iConsole.Data;
using iTerminal.Core.Dialogs.ToDo;

namespace iTerminal.Core.Commands;

public class ToDoListDialogCommand(ToDoListDialog toDoListDialog) : ShowDialogCommand<ToDoListDialog>(toDoListDialog)
{
    public override string Title => "Task List";
    public override string Description => "Open the task list: subjects, due dates and priorities, with done tasks kept below.";

    // Ctrl+Y. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Y, false, false, true);
}
