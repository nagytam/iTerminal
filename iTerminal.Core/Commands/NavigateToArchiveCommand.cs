using iConsole.Commands;
using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.Archives;

namespace iTerminal.Core.Commands;

/// <summary>
/// Enter on an archive opens it as a folder: the <c>archive://</c> drive, for ZIP, TAR, TAR.GZ,
/// TAR.BZ2, and — through 7-Zip — 7z, RAR, ISO and CAB. It goes through
/// <see cref="IContentModelService.SetUrlAsync(string, Url)"/> like any navigation, which is what
/// records the folder to come back to: the archive's top-level <c>..</c> leads there, with the
/// cursor on the archive.
/// </summary>
public class NavigateToArchiveCommand(
    IContentModelService contentModelService
    )
    : Command
{
    public override string Title => "Open archive";
    public override string Description => "Open the archive under the cursor as a folder (ZIP, TAR, TAR.GZ, TAR.BZ2; 7z, RAR, ISO, CAB with 7-Zip).";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.Enter, false, false, false);

    private Url? FocusedArchive()
    {
        var entry = contentModelService.GetFocusedModel()?.GetFocusedEntry();
        return entry is not null
               && entry.Url.ContainerType == ContainerType.FileSystem
               && entry.Url.PathType == PathType.File
               && ArchiveDriveService.LooksBrowsable(entry.Url.FullName)
            ? entry.Url
            : null;
    }

    public override bool CanExecute() => base.CanExecute() && FocusedArchive() is not null;

    public override async Task ExecuteAsync(object? arg)
    {
        var contentModel = contentModelService.GetFocusedModel();
        if (contentModel is not null && FocusedArchive() is { } archive)
        {
            await contentModelService.SetUrlAsync(contentModel.Name, ArchiveDriveUrl.Build(archive.FullName));
        }
    }
}
