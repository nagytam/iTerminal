using iConsole.Data;
using iTerminal.Core.Dialogs.Git;

namespace iTerminal.Core.Commands;

public class GitClientDialogCommand(GitClientDialog gitClientDialog) : ShowDialogCommand<GitClientDialog>(gitClientDialog)
{
    public override string Title => "Git";
    public override string Description => "Open the Git client (changed files, staging, commit history).";

    // Ctrl+G. Key constructor order is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.G, false, false, true);
}
