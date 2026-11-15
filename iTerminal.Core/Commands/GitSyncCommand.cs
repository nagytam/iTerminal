using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs.Git;

namespace iTerminal.Core.Commands;

/// <summary>
/// Git sync (Ctrl+Shift+G): opens <see cref="GitSyncDialog"/> for the repository the focused file panel is in. The dialog
/// shows the branches, sets how the pull reconciles and whether a push follows, and lists any conflicts. Offered only in a
/// folder of a local working tree (see <see cref="GitPullRebaseCommand.IsInWorkingTree"/>).
/// </summary>
public class GitSyncCommand(GitSyncDialog gitSyncDialog, IContentModelService contentModelService) : ShowDialogCommand<GitSyncDialog>(gitSyncDialog)
{
    public override string Title => "Git sync";

    // Key ctor is (consoleKey, shift, alt, control): Ctrl+Shift+G, the bottom bar's "Ctrl-Shift-G-Sync". Ctrl+D is the calendar
    // (a global command), so the sync cannot share it; Ctrl+G opens the Git client beside it.
    public override Key? Key { get; init; } = new Key(ConsoleKey.G, true, false, true);
    public override string Description => "Open the git sync: the branches, the pull and push options, and any conflicts.";

    public override bool CanExecute() => GitPullRebaseCommand.ResolveFolder(contentModelService) is not null;
}
