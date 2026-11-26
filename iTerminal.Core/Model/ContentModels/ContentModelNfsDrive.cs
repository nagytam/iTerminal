using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Nfs;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on the <c>nfs://</c> drive: a host's exports at <c>nfs://host/</c>, then an export's folders and files. The work
/// is <see cref="INfsDriveService"/>'s; like the MTP drive, the listing runs on a background task, because a mount over the
/// network can take seconds. A host and an export are read-only: nothing can be created, pasted or deleted at that level.
/// </summary>
public class ContentModelNfsDrive(
    string name,
    ISettingsService? settingsService,
    INfsDriveService nfsDriveService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.NFS;

    public override ContainerType ContainerType => ContainerType.Nfs;

    public override IFileSystem FileSystem => nfsDriveService;

    private NfsLocation Location => NfsDriveUrl.Parse(Url ?? new Url(Container.NFS));

    /// <summary>The drive root and a host's list of exports hold no files; only an export's contents can be changed.</summary>
    public override bool IsReadOnly => Location.Depth < 2;

    /// <summary>Exports are mounts, not files, so only what is listed inside a host or an export can be marked.</summary>
    public override bool CanMark => Location.Depth >= 2;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        nfsDriveService.ListAsync(url, cancellationToken);

    protected override string DescribeLocation() =>
        Location.Depth == 0 ? "NFS" : string.Format(UiText.Get("Nfs.Header", "NFS: {0}"), NfsDriveUrl.Describe(Location));

    public override string GetFooter()
    {
        var footer = base.GetFooter();
        return Location.Depth == 0 && StatusMessage.Length == 0
            ? $"{footer}│ {UiText.Get("Nfs.HostsNotListed", "NFS has no list of servers: type nfs://host/ to list its exports.")} "
            : footer;
    }
}
