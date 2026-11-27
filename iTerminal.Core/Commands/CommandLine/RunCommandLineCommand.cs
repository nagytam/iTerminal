using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model;

namespace iTerminal.Core.Commands.CommandLine;

/// <summary>
/// Enter in a panel while the command line holds something: runs it, as in mc, instead of opening
/// the entry under the cursor. Attached ahead of the Enter commands that open and run entries; it
/// declines — so those still apply — when the line is empty, when the command line is not on
/// screen (a command nobody can see must not run from a key meant to open a folder), or while a
/// command is already running. A command started here opens the terminal dialog on its output (a plain <c>cd</c> does not).
/// </summary>
public class RunCommandLineCommand(
    IContentModel ownModel,
    IContentModelService contentModelService,
    TerminalModel terminalModel,
    Func<bool> isCommandLineVisible,
    Func<Task>? showTerminalDialog = null)
    : Command
{
    public override string Title => "Run the command line";
    public override string Description => "Run the command typed on the command line, in this panel's folder, and show its output in the terminal dialog.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    public override bool CanExecute() =>
        ReferenceEquals(contentModelService.GetFocusedModel(), ownModel)
        && !terminalModel.IsRunning
        && !string.IsNullOrWhiteSpace(terminalModel.Command.Command)
        && isCommandLineVisible();

    public override async Task ExecuteAsync(object? args)
    {
        if (ownModel.Url is not null)
        {
            terminalModel.Command.WorkingDirectoryUrl = ownModel.Url;
            if (terminalModel.StartCommand() && showTerminalDialog is not null)
            {
                await showTerminalDialog();
            }
        }
    }
}
