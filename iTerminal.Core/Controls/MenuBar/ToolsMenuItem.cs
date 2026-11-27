using iTerminal.Core.Services;
using iConsole.Controls;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Extensions;

namespace iTerminal.Core.Controls.MenuBar;

public class ToolsMenuItem : MenuItem
{
    public ToolsMenuItem(
        TerminalDialog terminalDialog,
        DiskManagerDialog diskManagerDialog,
        NetworkManagerDialog networkManagerDialog,
        IPhoneBrowserDialog iPhoneBrowserDialog,
        iConsole.Interfaces.IContentModelService contentModelService,
        ThemeManagerDialog themeManagerDialog,
        SettingsDialog settingsDialog,
        TaskManagerDialog taskManagerDialog,
        ProcessManagerDialog processManagerDialog,
        SystemInfoDialog systemInfoDialog,
        IThemeService themeService,
        iTerminal.Core.Dialogs.Git.GitClientDialog? gitClientDialog = null,
        iTerminal.Core.Dialogs.Documents.DocumentManagerDialog? documentManagerDialog = null,
        NetworkBrowserDialog? networkBrowserDialog = null,
        iTerminal.Core.Dialogs.PackageManager.PackageManagerDialog? packageManagerDialog = null,
        iTerminal.Core.Dialogs.Calculator.CalculatorDialog? calculatorDialog = null,
        iTerminal.Core.Dialogs.UnitConverter.UnitConverterDialog? unitConverterDialog = null,
        iTerminal.Core.Dialogs.Calendar.CalendarDialog? calendarDialog = null,
        iTerminal.Core.Dialogs.Watch.WatchDialog? watchDialog = null,
        iTerminal.Core.Dialogs.ToDo.ToDoListDialog? toDoListDialog = null,
        iTerminal.Core.Dialogs.Mail.MailDialog? mailDialog = null,
        iTerminal.Core.Dialogs.PasswordManager.PasswordManagerDialog? passwordManagerDialog = null,
        iTerminal.Core.Services.ISettingsService? settingsService = null,
        iTerminal.Core.Dialogs.RuntimeDependencyDialog? runtimeDependencyDialog = null,
        DiskUsageDialog? diskUsageDialog = null,
        iTerminal.Core.Services.Elevation.IElevationService? elevationService = null,
        INotificationService? notificationService = null,
        DuplicateFinderDialog? duplicateFinderDialog = null,
        SystemSettingsDialog? systemSettingsDialog = null,
        iTerminal.Core.Dialogs.Web.WebBrowserDialog? webBrowserDialog = null,
        iTerminal.Core.Dialogs.Remote.RemoteConnectionManagerDialog? remoteConnectionManagerDialog = null,
        iTerminal.Core.Dialogs.Workspaces.WorkspaceManagerDialog? workspaceManagerDialog = null,
        LanguageDialog? languageDialog = null,
        iTerminal.Core.Dialogs.Torrents.TorrentManagerDialog? torrentManagerDialog = null,
        iTerminal.Core.Dialogs.Contacts.ContactManagerDialog? contactManagerDialog = null,
        iTerminal.Core.Dialogs.EnvVars.EnvironmentVariablesDialog? environmentVariablesDialog = null,
        iTerminal.Core.Dialogs.Benchmark.PerformanceBenchmarkDialog? performanceBenchmarkDialog = null,
        iTerminal.Core.Dialogs.Games.TicTacToeDialog? ticTacToeDialog = null,
        iTerminal.Core.Dialogs.Games.ChessDialog? chessDialog = null,
        iTerminal.Core.Dialogs.Games.GoDialog? goDialog = null,
        iTerminal.Core.Dialogs.Games.NineMensMorrisDialog? nineMensMorrisDialog = null,
        iTerminal.Core.Dialogs.Games.SnakeDialog? snakeDialog = null,
        iTerminal.Core.Dialogs.Games.TetrisDialog? tetrisDialog = null,
        iTerminal.Core.Dialogs.Games.RiskDialog? riskDialog = null,
        iTerminal.Core.Dialogs.Bluetooth.BluetoothDeviceManagerDialog? bluetoothDeviceManagerDialog = null,
        VirtualMachineManagerDialog? virtualMachineManagerDialog = null,
        iTerminal.Core.Dialogs.Games.JassDialog? jassDialog = null,
        iTerminal.Core.Dialogs.UsersAndGroups.UserGroupManagerDialog? userGroupManagerDialog = null,
        iTerminal.Core.Dialogs.Scanning.ScannerDialog? scannerDialog = null,
        iTerminal.Core.Dialogs.NetworkTools.NetworkToolsDialog? networkToolsDialog = null,
        iTerminal.Core.Dialogs.Plugins.PluginManagerDialog? pluginManagerDialog = null,
        iTerminal.Core.Dialogs.FileTransfer.FileTransferDialog? fileTransferDialog = null,
        iTerminal.Core.Dialogs.Voice.VoiceRecorderDialog? voiceRecorderDialog = null,
        iTerminal.Core.Dialogs.Recipes.RecipeManagerDialog? recipeManagerDialog = null,
        IEnumerable<IToolsMenuContribution>? widgetContributions = null)
        : base("Tools", null, [], themeService)
    {
        HotKeyChar = 'T';

        AddMenuItem(new MenuItem("Terminal\tF2",          new TerminalDialogCommand(terminalDialog), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Disk Manager\tAlt+F9", new DiskManagerDialogCommand(diskManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("Network Manager\tCtrl+F5",   new NetworkManagerDialogCommand(networkManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("iPhone Browser\tAlt+F11",      new IPhoneBrowserDialogCommand(iPhoneBrowserDialog), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Theme Manager\tAlt+F6",      new ThemeManagerDialogCommand(themeManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("Settings\tShift+F8",         new SettingsDialogCommand(settingsDialog), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Task Manager\tF9",           new TaskManagerDialogCommand(taskManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("Process Manager\tAlt+F12", new ProcessManagerDialogCommand(processManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        if (scannerDialog is not null)
        {
            AddMenuItem(new MenuItem("Scan…\tCtrl+Shift+I", new iTerminal.Core.Commands.ScannerDialogCommand(scannerDialog, contentModelService), [], themeService));
        }

        AddMenuItem(new MenuItem("System Info\tCtrl+F11",      new SystemInfoDialogCommand(systemInfoDialog), [], themeService));

        // The app's larger features were reachable only through hot keys nothing listed — no menu
        // led to the Git client, the document search or the calculator. Their labels are built
        // from the commands themselves, so the menu cannot teach a key the command does not answer to.
        Dictionary<string, List<iConsole.Commands.ICommand>> groups = new[] { "files", "media", "net", "office", "games", "sys" }.ToDictionary(k => k, _ => new List<iConsole.Commands.ICommand>());
        // Both chords (Ctrl+Shift+M and Ctrl+F7) come from the command, so the label cannot drift from them.
        // The video and radio players follow it from the widgets.
        groups["media"].Add(new iTerminal.Core.Commands.Music.MusicPanelsCommand(contentModelService));
        if (gitClientDialog is not null) groups["files"].Add(new GitClientDialogCommand(gitClientDialog));
        if (documentManagerDialog is not null) groups["files"].Add(new DocumentManagerDialogCommand(documentManagerDialog));
        if (networkBrowserDialog is not null) groups["net"].Add(new NetworkBrowserDialogCommand(networkBrowserDialog));
        if (networkToolsDialog is not null) groups["net"].Add(new NetworkToolsDialogCommand(networkToolsDialog));
        if (fileTransferDialog is not null) groups["net"].Add(new FileTransferDialogCommand(fileTransferDialog, contentModelService));
        if (packageManagerDialog is not null) groups["files"].Add(new PackageManagerDialogCommand(packageManagerDialog));
        if (calculatorDialog is not null) groups["office"].Add(new CalculatorDialogCommand(calculatorDialog));
        if (virtualMachineManagerDialog is not null && VirtualMachineManagerDialogCommand.IsSupported) groups["office"].Add(new VirtualMachineManagerDialogCommand(virtualMachineManagerDialog));
        if (userGroupManagerDialog is not null && UserGroupManagerDialogCommand.IsSupported) groups["sys"].Add(new UserGroupManagerDialogCommand(userGroupManagerDialog));
        if (unitConverterDialog is not null) groups["office"].Add(new UnitConverterDialogCommand(unitConverterDialog));
        if (calendarDialog is not null) groups["office"].Add(new CalendarDialogCommand(calendarDialog));
        if (watchDialog is not null) groups["office"].Add(new WatchDialogCommand(watchDialog));
        if (toDoListDialog is not null) groups["office"].Add(new ToDoListDialogCommand(toDoListDialog));
        if (mailDialog is not null) groups["net"].Add(new MailDialogCommand(mailDialog));
        if (webBrowserDialog is not null) groups["net"].Add(new WebBrowserDialogCommand(webBrowserDialog));
        if (torrentManagerDialog is not null) groups["net"].Add(new TorrentManagerDialogCommand(torrentManagerDialog));
        if (contactManagerDialog is not null) groups["office"].Add(new ContactManagerDialogCommand(contactManagerDialog));
        if (environmentVariablesDialog is not null) groups["files"].Add(new EnvironmentVariablesDialogCommand(environmentVariablesDialog));
        if (performanceBenchmarkDialog is not null) groups["sys"].Add(new PerformanceBenchmarkDialogCommand(performanceBenchmarkDialog));
        if (ticTacToeDialog is not null) groups["games"].Add(new TicTacToeDialogCommand(ticTacToeDialog));
        if (chessDialog is not null) groups["games"].Add(new ChessDialogCommand(chessDialog));
        if (goDialog is not null) groups["games"].Add(new GoDialogCommand(goDialog));
        if (nineMensMorrisDialog is not null) groups["games"].Add(new NineMensMorrisDialogCommand(nineMensMorrisDialog));
        if (snakeDialog is not null) groups["games"].Add(new SnakeDialogCommand(snakeDialog));
        if (tetrisDialog is not null) groups["games"].Add(new TetrisDialogCommand(tetrisDialog));
        if (riskDialog is not null) groups["games"].Add(new RiskDialogCommand(riskDialog));
        if (jassDialog is not null) groups["games"].Add(new JassDialogCommand(jassDialog));
        if (bluetoothDeviceManagerDialog is not null) groups["sys"].Add(new BluetoothDeviceManagerDialogCommand(bluetoothDeviceManagerDialog));
        if (remoteConnectionManagerDialog is not null) groups["net"].Add(new RemoteConnectionManagerDialogCommand(remoteConnectionManagerDialog));
        if (workspaceManagerDialog is not null) groups["sys"].Add(new WorkspaceManagerDialogCommand(workspaceManagerDialog));
        if (languageDialog is not null) groups["sys"].Add(new LanguageDialogCommand(languageDialog));
        if (pluginManagerDialog is not null) groups["sys"].Add(new PluginManagerDialogCommand(pluginManagerDialog));
        if (passwordManagerDialog is not null) groups["office"].Add(new PasswordManagerDialogCommand(passwordManagerDialog));
        if (voiceRecorderDialog is not null) groups["office"].Add(new VoiceRecorderDialogCommand(voiceRecorderDialog));
        if (recipeManagerDialog is not null) groups["office"].Add(new RecipeManagerDialogCommand(recipeManagerDialog));
        if (systemSettingsDialog is not null) groups["sys"].Add(new SystemSettingsDialogCommand(systemSettingsDialog));
        if (runtimeDependencyDialog is not null) groups["sys"].Add(new RuntimeDependencyDialogCommand(runtimeDependencyDialog));
        if (diskUsageDialog is not null) groups["files"].Add(new DiskUsageDialogCommand(diskUsageDialog, contentModelService));
        if (duplicateFinderDialog is not null) groups["files"].Add(new DuplicateFinderDialogCommand(duplicateFinderDialog, contentModelService));
        // Entries that features outside this assembly add (the online information widgets). They follow the entries above in their submenu.
        foreach (IToolsMenuContribution contribution in widgetContributions ?? [])
        {
            if (groups.TryGetValue(contribution.Group, out List<iConsole.Commands.ICommand>? group))
            {
                group.Add(contribution.Command);
            }
        }

        if (elevationService is not null)
        {
            groups["sys"].Add(new RestartAsAdministratorCommand(elevationService, notificationService));
            groups["sys"].Add(new RunAsAdministratorCommand(contentModelService, elevationService, notificationService));
        }

        // Related tools live in a submenu each — files, network, office, games, system — rather than in one
        // run of forty rows. The submenus follow the top-level entries, after a single separator.
        // The English titles are the source text; MenuItem.Localize translates them through Menu.<title>.
        Dictionary<string, string> groupTitles = new()
        {
            ["files"] = "Files",
            ["media"] = "Media",
            ["net"] = "Network",
            ["office"] = "Office",
            ["games"] = "Games",
            ["sys"] = "System",
        };
        List<MenuItem> submenus = [];
        foreach ((string key, List<iConsole.Commands.ICommand> group) in groups)
        {
            if (group.Count == 0)
            {
                continue;
            }

            List<MenuItem> entries = [];
            foreach (iConsole.Commands.ICommand command in group)
            {
                string label = string.IsNullOrEmpty(command.KeyDisplayText)
                    ? command.Title
                    : $"{command.Title}\t{command.KeyDisplayText}";
                entries.Add(new MenuItem(label, command, [], themeService));
            }
            submenus.Add(new MenuItem(groupTitles[key], null, entries, themeService));
        }

        if (submenus.Count > 0)
        {
            AddMenuItem(new MenuItem("-", null, [], themeService));
            foreach (MenuItem submenu in submenus)
            {
                AddMenuItem(submenu);
            }
        }
    }
}
