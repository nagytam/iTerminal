using iConsole.Commands;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Remote;

namespace iTerminal.Core.Commands;

public class FolderSelectorDialogCommand(
    IContentModelService contentModelService,
    FolderHistoryService folderHistoryService,
    DriveInfoModel driveInfoModel,
    FolderIndexModel folderIndexModel,
    IBeepService beepService,
    IDateTimeProviderService dateTimeProviderService,
    ISettingsService settingsService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IThemeService themeService,
    INetworkDiscoveryService networkDiscoveryService,
    INetworkProfileService networkProfileService,
    IRemoteConnectionService remoteConnectionService,
    IPasswordManager passwordManager,
    IPromptService? promptService,
    HelpDialog? helpDialog = null
    )
    : Command
{
    public override string Title => "Select drive or folder";
    public override string Description => "Open the folder selector to jump to another drive or folder.";

    public override Key? Key { get; init; } =
        new Key(ConsoleKey.F1, false, false, true);

    public override async Task ExecuteAsync(object? args)
    {
        if (Control is not null)
        {
            var dialog = new FolderSelectorDialog(
                contentModelService,
                folderHistoryService,
                driveInfoModel,
                folderIndexModel,
                beepService,
                dateTimeProviderService,
                settingsService,
                frequentExecutionGuardService,
                themeService,
                networkDiscoveryService,
                networkProfileService,
                remoteConnectionService,
                passwordManager,
                promptService,
                helpDialog);
            await Control.ShowDialogAsync(dialog);
        }
    }
}
