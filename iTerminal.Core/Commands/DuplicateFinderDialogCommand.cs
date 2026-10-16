using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+F7: find files with identical content under the folder in the panel
/// (<see cref="DuplicateFinderDialog"/>) — or under the folder the cursor is on. Local folders only.
/// </summary>
public class DuplicateFinderDialogCommand(
    DuplicateFinderDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<DuplicateFinderDialog>(dialog)
{
    public override string Title => "Find duplicate files";
    public override string Description => "Find files with identical content under this folder, then mark and delete the extra copies.";

    // Key ctor is (consoleKey, shift, alt, control). Ctrl+F7 is the music player, F7 find files.
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F7, true, false, true);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    /// <summary>The folder under the cursor when there is one, else the one the panel shows.</summary>
    private string? FolderToSearch()
    {
        var model = SourceModel;
        if (model?.Url is not { ContainerType: ContainerType.FileSystem } shown)
        {
            return null;
        }

        var under = FocusedEntry.ActionableUrl(model);
        var url = under is { PathType: PathType.Folder, ContainerType: ContainerType.FileSystem } ? under : shown;
        var path = url.FullName;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public override bool CanExecute() => FolderToSearch() is not null;

    protected override void OnBeforeShow(DuplicateFinderDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        if (FolderToSearch() is { } folder)
        {
            dialog.Load(folder, SourceModel?.Name);
        }
    }
}
