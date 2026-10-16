using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+F12: where the space went under the folder in the panel (<see cref="DiskUsageDialog"/>) —
/// or under the folder the cursor is on. Local folders only.
/// </summary>
public class DiskUsageDialogCommand(
    DiskUsageDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<DiskUsageDialog>(dialog)
{
    public override string Title => "Disk usage";
    public override string Description => "Show where the space went under this folder: every level by size, with bars.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F12, true, false, true);

    /// <summary>The panel the key acts on — the one the menu was opened over, when it runs from the menu.</summary>
    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    /// <summary>The folder under the cursor when there is one, else the one the panel shows.</summary>
    private string? FolderToMeasure()
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

    public override bool CanExecute() => FolderToMeasure() is not null;

    protected override void OnBeforeShow(DiskUsageDialog dialog)
    {
        // Handed over now, while the panel still has focus: the dialog takes it as it opens.
        if (FolderToMeasure() is { } folder)
        {
            dialog.Load(folder, SourceModel?.Name);
        }
    }
}
