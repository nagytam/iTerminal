using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Drives;

namespace iTerminal.Core.Commands;

/// <summary>
/// Ctrl+Shift+F11: mounts the ISO, VHD or VHDX under the cursor as a drive (or detaches it, with
/// the Unmount button) — <see cref="DiskImageDialog"/> with the file already in its box. Offered
/// only on a local image file; the disk manager's M is the way to type a path.
/// </summary>
public class MountDiskImageDialogCommand(
    DiskImageDialog dialog,
    IContentModelService contentModelService)
    : ShowDialogCommand<DiskImageDialog>(dialog)
{
    public override string Title => "Mount disk image";
    public override string Description => "Mount the ISO, VHD or VHDX under the cursor as a drive, or detach it.";

    // Key ctor is (consoleKey, shift, alt, control).
    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F11, true, false, true);

    private IContentModel? SourceModel =>
        contentModelService?.GetFocusedModel() ?? contentModelService?.GetLastContentModel();

    private string? ImagePath =>
        FocusedEntry.ActionableUrl(SourceModel) is { ContainerType: ContainerType.FileSystem, PathType: PathType.File } url
        && DriveActionsService.IsImage(url.Path)
            ? url.Path
            : null;

    public override bool CanExecute() => ImagePath is not null;

    protected override void OnBeforeShow(DiskImageDialog dialog)
    {
        if (ImagePath is { } path)
        {
            dialog.Load(path);
        }
    }
}
