using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Serilog;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Remote drive content model backed by SSH/SFTP. See <see cref="ContentModelRemoteDrive"/> for
/// the shared listing/caching/delegation behavior; this class only resolves SSH credentials
/// (saved profile password from the vault, profile key path, or the conventional default key) and
/// runs terminal commands over the SSH connection.
/// </summary>
public class ContentModelSshDrive(
    string name,
    ISettingsService settingsService,
    ISshFileSystemService sshFileSystemService,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IMemoryCacheService memoryCacheService,
    ILogger logger,
    INetworkProfileService? networkProfileService = null,
    IPasswordManager? passwordManager = null
    )
    : ContentModelRemoteDrive(name, settingsService, sshFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger),
      Services.Shell.IShellLaunchSource
{
    /// <summary>Ctrl+O: a login shell on the host, in this folder, through the system's ssh.</summary>
    public Services.Shell.ShellLaunch? GetShellLaunch(string? configuredShell) =>
        Url is { Host.Length: > 0 } url ? Services.Shell.ShellLaunch.Ssh(url.Username, url.Host, url.Path) : null;

    protected override void Connect(Url url)
    {
        var profile = networkProfileService?.GetAll().FirstOrDefault(p =>
            p.Protocol == NetworkProtocol.Ssh
            && string.Equals(p.Host, url.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Username, url.Username, StringComparison.OrdinalIgnoreCase));

        string? password = null;
        var privateKeyPath = profile?.PrivateKeyPath;
        if (profile is not null && profile.HasSavedCredential && profile.SshAuthMethod == SshAuthMethod.Password)
        {
            password = passwordManager?.GetSecret(profile.Id);
        }
        if (string.IsNullOrEmpty(privateKeyPath) && string.IsNullOrEmpty(password))
        {
            // No saved connection profile: fall back to the conventional default SSH key
            // location (portable across users/OSes, unlike a hardcoded path).
            privateKeyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".ssh", "id_rsa");
        }

        sshFileSystemService.Connect(url.Host, url.Username, password, privateKeyPath);
    }

    public override Task RunTerminalCommandAsync(ITerminalModel terminalModel)
    {
        sshFileSystemService.Execute(terminalModel.Command);
        return Task.CompletedTask;
    }
}
