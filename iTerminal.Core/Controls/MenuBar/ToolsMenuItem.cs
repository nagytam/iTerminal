using iConsole.Controls;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;

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
        iTerminal.Core.Dialogs.Stocks.StockBrowserDialog? stockBrowserDialog = null,
        iTerminal.Core.Dialogs.Map.MapDialog? mapDialog = null,
        iTerminal.Core.Dialogs.Remote.RemoteConnectionManagerDialog? remoteConnectionManagerDialog = null,
        iTerminal.Core.Dialogs.Translator.TranslatorDialog? translatorDialog = null,
        iTerminal.Core.Dialogs.CurrencyConverter.CurrencyConverterDialog? currencyConverterDialog = null,
        iTerminal.Core.Dialogs.Workspaces.WorkspaceManagerDialog? workspaceManagerDialog = null,
        LanguageDialog? languageDialog = null,
        iTerminal.Core.Dialogs.Torrents.TorrentManagerDialog? torrentManagerDialog = null,
        iTerminal.Core.Dialogs.Contacts.ContactManagerDialog? contactManagerDialog = null,
        iTerminal.Core.Dialogs.EnvVars.EnvironmentVariablesDialog? environmentVariablesDialog = null,
        iTerminal.Core.Dialogs.Encyclopedia.EncyclopediaDialog? encyclopediaDialog = null,
        iTerminal.Core.Dialogs.Benchmark.PerformanceBenchmarkDialog? performanceBenchmarkDialog = null,
        iTerminal.Core.Dialogs.Games.TicTacToeDialog? ticTacToeDialog = null,
        iTerminal.Core.Dialogs.Games.ChessDialog? chessDialog = null,
        iTerminal.Core.Dialogs.Games.GoDialog? goDialog = null,
        iTerminal.Core.Dialogs.Games.NineMensMorrisDialog? nineMensMorrisDialog = null,
        iTerminal.Core.Dialogs.Games.SnakeDialog? snakeDialog = null,
        iTerminal.Core.Dialogs.Games.TetrisDialog? tetrisDialog = null,
        iTerminal.Core.Dialogs.Bluetooth.BluetoothDeviceManagerDialog? bluetoothDeviceManagerDialog = null,
        VirtualMachineManagerDialog? virtualMachineManagerDialog = null,
        iTerminal.Core.Dialogs.Games.JassDialog? jassDialog = null)
        : base("Tools", null, [], themeService)
    {
        HotKeyChar = 'T';

        AddMenuItem(new MenuItem("Terminal\tF2",          new TerminalDialogCommand(terminalDialog), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Disk Manager\tAlt+F9", new DiskManagerDialogCommand(diskManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("Network Manager\tCtrl+F5",   new NetworkManagerDialogCommand(networkManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("iPhone Browser\tAlt+F11",      new IPhoneBrowserDialogCommand(iPhoneBrowserDialog), [], themeService));
        AddMenuItem(new MenuItem("Music Player\tCtrl+F7", new iTerminal.Core.Commands.Music.MusicPanelsCommand(contentModelService), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Theme Manager\tAlt+F6",      new ThemeManagerDialogCommand(themeManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("Settings\tShift+F8",         new SettingsDialogCommand(settingsDialog), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("Task Manager\tF9",           new TaskManagerDialogCommand(taskManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("Process Manager\tAlt+F12", new ProcessManagerDialogCommand(processManagerDialog), [], themeService));
        AddMenuItem(new MenuItem("-", null, [], themeService));
        AddMenuItem(new MenuItem("System Info\tCtrl+F11",      new SystemInfoDialogCommand(systemInfoDialog), [], themeService));

        // The app's larger features were reachable only through hot keys nothing listed — no menu
        // led to the Git client, the document search or the calculator. Their labels are built
        // from the commands themselves, so the menu cannot teach a key the command does not answer to.
        Dictionary<string, List<iConsole.Commands.ICommand>> groups = new[] { "files", "net", "office", "games", "sys" }.ToDictionary(k => k, _ => new List<iConsole.Commands.ICommand>());
        if (gitClientDialog is not null) groups["files"].Add(new GitClientDialogCommand(gitClientDialog));
        if (documentManagerDialog is not null) groups["files"].Add(new DocumentManagerDialogCommand(documentManagerDialog));
        if (networkBrowserDialog is not null) groups["net"].Add(new NetworkBrowserDialogCommand(networkBrowserDialog));
        if (packageManagerDialog is not null) groups["files"].Add(new PackageManagerDialogCommand(packageManagerDialog));
        if (stockBrowserDialog is not null) groups["office"].Add(new StockBrowserDialogCommand(stockBrowserDialog));
        if (calculatorDialog is not null) groups["office"].Add(new CalculatorDialogCommand(calculatorDialog));
        if (virtualMachineManagerDialog is not null && VirtualMachineManagerDialogCommand.IsSupported) groups["office"].Add(new VirtualMachineManagerDialogCommand(virtualMachineManagerDialog));
        if (unitConverterDialog is not null) groups["office"].Add(new UnitConverterDialogCommand(unitConverterDialog));
        if (calendarDialog is not null) groups["office"].Add(new CalendarDialogCommand(calendarDialog));
        if (toDoListDialog is not null) groups["office"].Add(new ToDoListDialogCommand(toDoListDialog));
        if (mailDialog is not null) groups["net"].Add(new MailDialogCommand(mailDialog));
        if (webBrowserDialog is not null) groups["net"].Add(new WebBrowserDialogCommand(webBrowserDialog));
        if (mapDialog is not null) groups["office"].Add(new MapDialogCommand(mapDialog));
        if (translatorDialog is not null) groups["office"].Add(new TranslatorDialogCommand(translatorDialog));
        if (currencyConverterDialog is not null) groups["office"].Add(new CurrencyConverterDialogCommand(currencyConverterDialog));
        if (torrentManagerDialog is not null) groups["net"].Add(new TorrentManagerDialogCommand(torrentManagerDialog));
        if (contactManagerDialog is not null) groups["office"].Add(new ContactManagerDialogCommand(contactManagerDialog));
        if (environmentVariablesDialog is not null) groups["files"].Add(new EnvironmentVariablesDialogCommand(environmentVariablesDialog));
        if (encyclopediaDialog is not null) groups["office"].Add(new EncyclopediaDialogCommand(encyclopediaDialog));
        if (performanceBenchmarkDialog is not null) groups["sys"].Add(new PerformanceBenchmarkDialogCommand(performanceBenchmarkDialog));
        if (ticTacToeDialog is not null) groups["games"].Add(new TicTacToeDialogCommand(ticTacToeDialog));
        if (chessDialog is not null) groups["games"].Add(new ChessDialogCommand(chessDialog));
        if (goDialog is not null) groups["games"].Add(new GoDialogCommand(goDialog));
        if (nineMensMorrisDialog is not null) groups["games"].Add(new NineMensMorrisDialogCommand(nineMensMorrisDialog));
        if (snakeDialog is not null) groups["games"].Add(new SnakeDialogCommand(snakeDialog));
        if (tetrisDialog is not null) groups["games"].Add(new TetrisDialogCommand(tetrisDialog));
        if (jassDialog is not null) groups["games"].Add(new JassDialogCommand(jassDialog));
        if (bluetoothDeviceManagerDialog is not null) groups["sys"].Add(new BluetoothDeviceManagerDialogCommand(bluetoothDeviceManagerDialog));
        if (remoteConnectionManagerDialog is not null) groups["net"].Add(new RemoteConnectionManagerDialogCommand(remoteConnectionManagerDialog));
        if (workspaceManagerDialog is not null) groups["sys"].Add(new WorkspaceManagerDialogCommand(workspaceManagerDialog));
        if (languageDialog is not null) groups["sys"].Add(new LanguageDialogCommand(languageDialog));
        if (passwordManagerDialog is not null) groups["office"].Add(new PasswordManagerDialogCommand(passwordManagerDialog));
        if (systemSettingsDialog is not null) groups["sys"].Add(new SystemSettingsDialogCommand(systemSettingsDialog));
        if (runtimeDependencyDialog is not null) groups["sys"].Add(new RuntimeDependencyDialogCommand(runtimeDependencyDialog));
        if (diskUsageDialog is not null) groups["files"].Add(new DiskUsageDialogCommand(diskUsageDialog, contentModelService));
        if (duplicateFinderDialog is not null) groups["files"].Add(new DuplicateFinderDialogCommand(duplicateFinderDialog, contentModelService));
        if (elevationService is not null)
        {
            groups["sys"].Add(new RestartAsAdministratorCommand(elevationService, notificationService));
            groups["sys"].Add(new RunAsAdministratorCommand(contentModelService, elevationService, notificationService));
        }

        // Related tools sit together between separators — files, network, everyday helpers, games, system —
        // rather than in one unbroken run of forty rows.
        foreach (List<iConsole.Commands.ICommand> group in groups.Values.Where(g => g.Count > 0))
        {
            AddMenuItem(new MenuItem("-", null, [], themeService));
            foreach (iConsole.Commands.ICommand command in group)
            {
                string label = string.IsNullOrEmpty(command.KeyDisplayText)
                    ? command.Title
                    : $"{command.Title}	{command.KeyDisplayText}";
                AddMenuItem(new MenuItem(label, command, [], themeService));
            }
        }
    }
}
