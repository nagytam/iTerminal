using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Mtp;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on the <c>mtp://</c> drive: the connected phones and cameras at the root, a device's
/// storages below it, then the folders. All of the work is <see cref="IMtpDriveService"/>'s; like
/// the docker drive the listing runs on a background task, because the device (or the tool in front
/// of it) can take seconds to answer.
/// </summary>
public class ContentModelMtpDrive(
    string name,
    ISettingsService? settingsService,
    IMtpDriveService mtpDriveService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.MTP;

    public override ContainerType ContainerType => ContainerType.Mtp;

    public override IFileSystem FileSystem => mtpDriveService;

    private bool IsDeviceOrStorageList => MtpDriveService.Split(Url ?? new Url(Container.MTP)).Path.Length == 0;

    /// <summary>Devices and storages are listed, but nothing can be created or copied into them.</summary>
    public override bool IsReadOnly => IsDeviceOrStorageList;

    /// <summary>Devices and storages are not files: nothing there may be marked for F5 or F8.</summary>
    public override bool CanMark => !IsDeviceOrStorageList;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        mtpDriveService.ListAsync(url, cancellationToken);

    protected override string DescribeLocation()
    {
        if (mtpDriveService.Unavailable.Length > 0)
        {
            return "mtp: unavailable";
        }

        var (device, path) = MtpDriveService.Split(Url ?? new Url(Container.MTP));
        return device.Length == 0 ? "mtp: devices" : $"mtp: {device}/{path}";
    }

    public override string GetFooter()
    {
        var footer = base.GetFooter();
        return StatusMessage.Length == 0 && mtpDriveService.Unavailable.Length > 0
            ? $"{footer}│ {mtpDriveService.Unavailable} "
            : footer;
    }
}
