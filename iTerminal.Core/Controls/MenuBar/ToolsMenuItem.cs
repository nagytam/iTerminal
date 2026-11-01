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
        iTerminal.Core.Dialogs.Encyclopedia.EncyclopediaDialog? encyclopediaDialog = null,
        iTerminal.Core.Dialogs.Benchmark.PerformanceBenchmarkDialog? performanceBenchmarkDialog = null,
        iTerminal.Core.Dialogs.Games.GamesDialog? gamesDialog = null,
        iTerminal.Core.Dialogs.Bluetooth.BluetoothDeviceManagerDialog? bluetoothDeviceManagerDialog = null,
        VirtualMachineManagerDialog? virtualMachineManagerDialog = null)
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
        List<iConsole.Commands.ICommand> features = [];
        if (gitClientDialog is not null) features.Add(new GitClientDialogCommand(gitClientDialog));
        if (documentManagerDialog is not null) features.Add(new DocumentManagerDialogCommand(documentManagerDialog));
        if (networkBrowserDialog is not null) features.Add(new NetworkBrowserDialogCommand(networkBrowserDialog));
        if (packageManagerDialog is not null) features.Add(new PackageManagerDialogCommand(packageManagerDialog));
        if (stockBrowserDialog is not null) features.Add(new StockBrowserDialogCommand(stockBrowserDialog));
        if (calculatorDialog is not null) features.Add(new CalculatorDialogCommand(calculatorDialog));
        if (virtualMachineManagerDialog is not null && VirtualMachineManagerDialogCommand.IsSupported) features.Add(new VirtualMachineManagerDialogCommand(virtualMachineManagerDialog));
        if (unitConverterDialog is not null) features.Add(new UnitConverterDialogCommand(unitConverterDialog));
        if (calendarDialog is not null) features.Add(new CalendarDialogCommand(calendarDialog));
        if (toDoListDialog is not null) features.Add(new ToDoListDialogCommand(toDoListDialog));
        if (mailDialog is not null) features.Add(new MailDialogCommand(mailDialog));
        if (webBrowserDialog is not null) features.Add(new WebBrowserDialogCommand(webBrowserDialog));
        if (mapDialog is not null) features.Add(new MapDialogCommand(mapDialog));
        if (translatorDialog is not null) features.Add(new TranslatorDialogCommand(translatorDialog));
        if (currencyConverterDialog is not null) features.Add(new CurrencyConverterDialogCommand(currencyConverterDialog));
        if (torrentManagerDialog is not null) features.Add(new TorrentManagerDialogCommand(torrentManagerDialog));
        if (contactManagerDialog is not null) features.Add(new ContactManagerDialogCommand(contactManagerDialog));
        if (encyclopediaDialog is not null) features.Add(new EncyclopediaDialogCommand(encyclopediaDialog));
        if (performanceBenchmarkDialog is not null) features.Add(new PerformanceBenchmarkDialogCommand(performanceBenchmarkDialog));
        if (gamesDialog is not null) features.Add(new GamesDialogCommand(gamesDialog));
        if (bluetoothDeviceManagerDialog is not null) features.Add(new BluetoothDeviceManagerDialogCommand(bluetoothDeviceManagerDialog));
        if (remoteConnectionManagerDialog is not null) features.Add(new RemoteConnectionManagerDialogCommand(remoteConnectionManagerDialog));
        if (workspaceManagerDialog is not null) features.Add(new WorkspaceManagerDialogCommand(workspaceManagerDialog));
        if (languageDialog is not null) features.Add(new LanguageDialogCommand(languageDialog));
        if (passwordManagerDialog is not null) features.Add(new PasswordManagerDialogCommand(passwordManagerDialog));
        if (systemSettingsDialog is not null) features.Add(new SystemSettingsDialogCommand(systemSettingsDialog));
        if (runtimeDependencyDialog is not null) features.Add(new RuntimeDependencyDialogCommand(runtimeDependencyDialog));
        if (diskUsageDialog is not null) features.Add(new DiskUsageDialogCommand(diskUsageDialog, contentModelService));
        if (duplicateFinderDialog is not null) features.Add(new DuplicateFinderDialogCommand(duplicateFinderDialog, contentModelService));
        if (elevationService is not null)
        {
            features.Add(new RestartAsAdministratorCommand(elevationService, notificationService));
            features.Add(new RunAsAdministratorCommand(contentModelService, elevationService, notificationService));
        }

        if (features.Count > 0)
        {
            AddMenuItem(new MenuItem("-", null, [], themeService));
            foreach (var command in features)
            {
                string label = string.IsNullOrEmpty(command.KeyDisplayText)
                    ? command.Title
                    : $"{command.Title}\t{command.KeyDisplayText}";
                AddMenuItem(new MenuItem(label, command, [], themeService));
            }
        }
    }
}

