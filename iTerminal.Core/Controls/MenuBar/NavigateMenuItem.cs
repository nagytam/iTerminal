using iConsole.Controls;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.MenuBar;

public class NavigateMenuItem : MenuItem
{
    public NavigateMenuItem(
        IContentModelService contentModelService,
        FolderHistoryService folderHistoryService,
        DriveInfoModel driveInfoModel,
        FolderIndexModel folderIndexModel,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        ISettingsService settingsService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        Content content,
        IThemeService themeService,
        HelpDialog? helpDialog = null,
        InteractiveShellCommand? shellCommand = null,
        iTerminal.Core.Dialogs.SortDialog? sortDialog = null)
        : base("Navigate", null, [], themeService)
    {
        HotKeyChar = 'N';

        var folderSelectorCommand = new FolderSelectorDialogCommand(
            contentModelService,
            folderHistoryService,
            driveInfoModel,
            folderIndexModel,
            beepService,
            dateTimeProviderService,
            settingsService,
            frequentExecutionGuardService,
            themeService,
            helpDialog);

        AddMenuItem(new MenuItem("Go To Folder\tCtrl+F1",  folderSelectorCommand, [], themeService));
        AddMenuItem(new MenuItem("Switch Panels\tCtrl+U",  new SwitchPanelsCommand(contentModelService), [], themeService));
        if (sortDialog is not null)
        {
            AddMenuItem(new MenuItem("Sort By…\tShift+F11", new SortDialogCommand(sortDialog, contentModelService), [], themeService));
        }
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Git History\tCtrl+F6", new iTerminal.Core.Commands.Drives.GitDriveCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Docker Containers\tCtrl+F9", new iTerminal.Core.Commands.Drives.DockerDriveCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("Recycle Bin\tCtrl+F8", new iTerminal.Core.Commands.Drives.RecycleDriveCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        if (shellCommand is not null)
        {
            AddMenuItem(new MenuItem("Shell\tCtrl+O", shellCommand, [], themeService));
        }
        AddMenuItem(new MenuItem("Command Log\tCtrl+Shift+O", new ToggleTerminalCommand(content), [], themeService));
    }
}
