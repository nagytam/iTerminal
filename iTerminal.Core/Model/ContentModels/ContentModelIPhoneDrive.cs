using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Remote drive content model backed by an iOS device over AFC. See <see cref="ContentModelRemoteDrive"/>
/// for the shared listing/caching/delegation behavior; this class only maps the <c>iphone://&lt;udid&gt;/</c>
/// URL host onto an AFC <see cref="IIPhoneFileSystemService.Connect"/> call. There is no interactive
/// shell on a device, so <see cref="RunTerminalCommandAsync"/> is a no-op.
/// </summary>
public class ContentModelIPhoneDrive(
    string name,
    ISettingsService settingsService,
    IIPhoneFileSystemService iPhoneFileSystemService,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IMemoryCacheService memoryCacheService,
    ILogger logger)
    : ContentModelRemoteDrive(name, settingsService, iPhoneFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger)
{
    protected override void Connect(Url url)
    {
        iPhoneFileSystemService.Connect(url.Host);
    }

    protected override string FormatConnectionError(Url url, Exception exception) =>
        $"iPhone connection error: {url.Host}: {exception.Message}";
}
