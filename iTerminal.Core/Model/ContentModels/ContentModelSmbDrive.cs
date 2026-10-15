using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Remote drive content model backed by SMB. See <see cref="ContentModelRemoteDrive"/> for the
/// shared listing/caching/delegation behavior; this class only resolves the saved share
/// credentials and connects the SMB service.
/// </summary>
public class ContentModelSmbDrive(
    string name,
    ISettingsService settingsService,
    ISmbFileSystemService smbFileSystemService,
    INetworkProfileService networkProfileService,
    IPasswordManager passwordManager,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IMemoryCacheService memoryCacheService,
    ILogger logger
    )
    : ContentModelRemoteDrive(name, settingsService, smbFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger)
{
    protected override void Connect(Url url)
    {
        var profile = networkProfileService.GetAll().FirstOrDefault(p =>
            p.Protocol == NetworkProtocol.Smb
            && string.Equals(p.Host, url.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.ShareName, url.Share, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Username, url.Username, StringComparison.OrdinalIgnoreCase));
        var password = profile is not null && profile.HasSavedCredential
            ? passwordManager.GetSecret(profile.Id) ?? string.Empty
            : string.Empty;
        smbFileSystemService.Connect(url.Host, url.Share, url.Username, password);
    }

    protected override string FormatConnectionError(Url url, Exception exception) =>
        $"Connection error: {url.Host}\\{url.Share}: {exception.Message}";
}
