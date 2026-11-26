using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Obex;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// A panel on the <c>obex://</c> drive: the paired Bluetooth devices at the root, then a device's
/// folders. All of the work is <see cref="IObexDriveService"/>'s; like the MTP drive the listing runs on
/// a background task, because a device has to accept the connection on its own screen and can take a
/// long time to answer.
/// </summary>
public class ContentModelObexDrive(
    string name,
    ISettingsService? settingsService,
    IObexDriveService obexDriveService,
    FolderHistoryService? folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    ILogger? logger = null)
    : ContentModelAsyncDrive(name, settingsService, folderHistoryService, frequentExecutionGuardService, logger)
{
    protected override string Scheme => Container.OBEX;

    public override ContainerType ContainerType => ContainerType.Obex;

    public override IFileSystem FileSystem => obexDriveService;

    private bool IsDeviceList => ObexDriveService.Split(Url ?? new Url(Container.OBEX)).Address.Length == 0;

    /// <summary>The device list is listed, but nothing can be created or copied into it.</summary>
    public override bool IsReadOnly => IsDeviceList;

    /// <summary>Devices are not files: nothing there may be marked for F5 or F8.</summary>
    public override bool CanMark => !IsDeviceList;

    protected override Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken) =>
        obexDriveService.ListAsync(url, cancellationToken);

    protected override string DescribeLocation()
    {
        if (obexDriveService.Unavailable.Length > 0)
        {
            return "obex: unavailable";
        }

        var (address, path) = ObexDriveService.Split(Url ?? new Url(Container.OBEX));
        return address.Length == 0 ? "obex: devices" : $"obex: {address}/{path}";
    }

    public override string GetFooter()
    {
        var footer = base.GetFooter();
        return StatusMessage.Length == 0 && obexDriveService.Unavailable.Length > 0
            ? $"{footer}│ {obexDriveService.Unavailable} "
            : footer;
    }
}
