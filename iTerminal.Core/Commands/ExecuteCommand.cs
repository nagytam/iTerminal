using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Model;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Commands;

/// <summary>
/// Enter on a program (an executable file) runs it in the command log, from its own folder. Any other file is
/// left to <see cref="OpenWithSystemDefaultCommand"/>, which opens it outside the application. A folder is opened
/// by <see cref="NavigateToFolderCommand"/>.
/// </summary>
public class ExecuteCommand(
    IContentModel contentModel,
    TerminalModel terminalModel,
    TaskManager taskManager
    )
    : Command
{
    public override string Title => "Run program";
    public override string Description => "Run the selected program in the command log.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute()
    {
        var entry = contentModel.FilteredEntries[(int)contentModel.CursorPosition.Y];
        return base.CanExecute() && ProgramFile.IsRunnable(FileAssociationRunner.LocalFilePath(entry.Url));
    }

    public override Task ExecuteAsync(object? arg)
    {
        var entry = contentModel.FilteredEntries[(int)contentModel.CursorPosition.Y];

        terminalModel.Command.Command = ProgramFile.CommandFor(entry.Name);
        terminalModel.Command.WorkingDirectoryUrl = Path.GetDirectoryName(entry.FullName) ?? string.Empty; // TODO entry.Directory
        var executeCommandTask = new ExecuteCommandTask()
        {
            // Without the panel the task has nothing to run: it completes at once and the command never starts.
            ContentModel = contentModel,
            TerminalModel = terminalModel,
        };
        taskManager.Add(executeCommandTask);
        return Task.CompletedTask;
    }
}
