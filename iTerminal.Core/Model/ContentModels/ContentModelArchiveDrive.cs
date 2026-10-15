using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on the <c>archive://</c> drive: an archive's folders and files, read-only. All of the work
/// is <see cref="IArchiveDriveService"/>'s; the listing comes in on a background task, because
/// reading a TAR.GZ's listing means decompressing all of it.
/// </summary>
public class ContentModelArchiveDrive(
    string name,
    ISettingsService? settingsService,
    IArchiveDriveService archiveDriveService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger),
      Services.Shell.IShellLaunchSource
{
    /// <summary>Ctrl+O: a local shell in the folder the archive is in.</summary>
    public Services.Shell.ShellLaunch? GetShellLaunch(string? configuredShell)
    {
        var (archivePath, _) = ArchiveDriveUrl.Parse(Url);
        return Path.GetDirectoryName(archivePath) is { Length: > 0 } folder
            ? Services.Shell.ShellLaunch.Local(folder, configuredShell)
            : null;
    }

    protected override string Scheme => Container.ARCHIVE;

    public override ContainerType ContainerType => ContainerType.Archive;

    public override IFileSystem FileSystem => archiveDriveService;

    public override bool IsReadOnly => true;

    /// <summary>The archive's top level is where <c>..</c> leaves it — back to the folder the archive is in.</summary>
    protected override bool IsDriveRoot(Url url) => ArchiveDriveUrl.Parse(url).InnerPath.Length == 0;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        archiveDriveService.ListAsync(url, cancellationToken);

    protected override string DescribeLocation()
    {
        var (archivePath, innerPath) = ArchiveDriveUrl.Parse(Url);
        return $"{Path.GetFileName(archivePath)}:/{innerPath}";
    }
}
