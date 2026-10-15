using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Model;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Commands;

public class ExecuteCommand(
    IContentModel contentModel,
    TerminalModel terminalModel,
    TaskManager taskManager
    ) 
    : Command
{
    public override string Title => "Open or run";
    public override string Description => "Open the selected folder or run the selected file.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        var canExecute = base.CanExecute();
        var entry = contentModel.FilteredEntries[(int)contentModel.CursorPosition.Y];
        if ((entry.Url.ContainerType == ContainerType.FileSystem) &&
            (entry.Url.PathType == PathType.File))
        {
            canExecute = true;
        }
        return canExecute;
    }

    public override Task ExecuteAsync(object? arg)
    {
        var entry = contentModel.FilteredEntries[(int)contentModel.CursorPosition.Y];

        var command = entry.Name;
        //switch (System.IO.Path.GetExtension(command).ToLowerInvariant())
        //{
        //    case ".ps1":
        //        {
        //            command = "powershell -f " + command;
        //            break;
        //        }
        //}
        terminalModel.Command.Command = command;

        terminalModel.Command.WorkingDirectoryUrl = Path.GetDirectoryName(entry.FullName); // TODO entry.Directory
        var executeCommandTask = new ExecuteCommandTask() 
        { 
            TerminalModel = terminalModel 
        };
        taskManager.Add(executeCommandTask);
        return Task.CompletedTask;
    }
}
