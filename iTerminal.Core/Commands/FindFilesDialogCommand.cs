using iConsole.Data;
using iTerminal.Core.Dialogs.Search;

namespace iTerminal.Core.Commands;

/// <summary>
/// F7: find files. F7 used to create a folder, which moved to Shift+F7 — the plain function keys
/// are the ones the hot-key bar advertises, and searching is reached for far more often than
/// creating a folder.
/// </summary>
public class FindFilesDialogCommand(FindFilesDialog findFilesDialog)
    : ShowDialogCommand<FindFilesDialog>(findFilesDialog)
{
    public override string Title => "Find files";
    public override string Description => "Search for files by name and content; results can be listed in a panel.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F7, false, false, false);
}
