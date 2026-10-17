using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Remote drive content model backed by FTP (<c>ftp://[user@]host[:port]/path</c>). See
/// <see cref="ContentModelRemoteDrive"/> for the shared listing/caching/delegation behavior; this
/// class resolves the login — a saved FTP connection's password from the vault, else anonymous —
/// and lets a cross-drive folder copy walk the folder through <see cref="IFolderListingFileSystem"/>.
/// </summary>
public class ContentModelFtpDrive(
    string name,
    ISettingsService settingsService,
    IFtpFileSystemService ftpFileSystemService,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IMemoryCacheService memoryCacheService,
    ILogger logger,
    INetworkProfileService? networkProfileService = null,
    IPasswordManager? passwordManager = null)
    : ContentModelRemoteDrive(name, settingsService, ftpFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger),
      IFolderListingFileSystem
{
    protected override void Connect(Url url)
    {
        var profile = string.IsNullOrEmpty(url.Username)
            ? null
            : networkProfileService?.GetAll().FirstOrDefault(p =>
                p.Protocol == NetworkProtocol.Ftp
                && string.Equals(p.Host, url.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Username, url.Username, StringComparison.OrdinalIgnoreCase));

        var password = profile is not null && profile.HasSavedCredential
            ? passwordManager?.GetSecret(profile.Id)
            : null;
        ftpFileSystemService.Connect(url.Host, url.Username, password);
    }

    protected override string FormatConnectionError(Url url, Exception exception) =>
        $"FTP connection error: {url.Host}: {exception.Message}";

    public override IEnumerable<Url> ListChildren(Url folderUrl) =>
        ftpFileSystemService is IFolderListingFileSystem listing
            ? listing.ListChildren(folderUrl)
            : [];
}
