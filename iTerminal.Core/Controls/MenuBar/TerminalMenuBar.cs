using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Core.Controls.MenuBar;

public class TerminalMenuBar : iConsole.Controls.MenuBar
{
    private const int DateTimeWidth = 19;
    private const int TaskIndicatorWidth = 10;
    private const int WeatherIndicatorWidth = 20;
    private const int QuoteBtcIndicatorWidth = 18;
    private const int QuoteEthIndicatorWidth = 17;

    private ISettingsService _settingsService { get; set; }
    private iConsole.Controls.StatusBar.StatusBar _statusBarTopRight { get; set; }
    private iConsole.Controls.StatusBar.TaskStatus _taskStatus { get; set; }
    private WeatherIndicator _weather { get; set; }
    private QuoteIndicator _quoteETH { get; set; }
    private QuoteIndicator _quoteBTC { get; set; }

    private bool _isWeatherIndicatorEnabled = true;
    private bool _isQuotesIndicatorEnabled = true;

    public TerminalMenuBar(
        IThemeService themeService,
        IContentModelService contentModelService,
        ISettingsService settingsService,
        WeatherService weatherService,
        OnlineQuoteService onlineQuoteService,
        TaskManager taskManager,
        // File
        FileViewerDialog fileViewerDialog,
        TextFileEditorDialog textFileEditorDialog,
        CreateFolderDialog createFolderDialog,
        CreateFileDialog createFileDialog,
        RenameDialog renameDialog,
        // Edit
        CopyDialog copyDialog,
        MoveDialog moveDialog,
        DeleteDialog deleteDialog,
        // Navigate
        FolderHistoryService folderHistoryService,
        DriveInfoModel driveInfoModel,
        FolderIndexModel folderIndexModel,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IFrequentExecutionGuardService frequentExecutionGuardService,
        Content content,
        // Tools
        TerminalDialog terminalDialog,
        DiskManagerDialog diskManagerDialog,
        NetworkManagerDialog networkManagerDialog,
        IPhoneBrowserDialog iPhoneBrowserDialog,
        ThemeManagerDialog themeManagerDialog,
        SettingsDialog settingsDialog,
        TaskManagerDialog taskManagerDialog,
        ProcessManagerDialog processManagerDialog,
        SystemInfoDialog systemInfoDialog,
        // Help / About
        HelpDialog helpDialog,
        AboutDialog aboutDialog,
        iTerminal.Core.Dialogs.Music.MusicFileViewerDialog? musicFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.VideoFileViewerDialog? videoFileViewerDialog = null,
        iTerminal.Core.Dialogs.Video.ImageFileViewerDialog? imageFileViewerDialog = null,
        // Tools, the larger features (optional so the menu can be built without them)
        iTerminal.Core.Dialogs.Git.GitClientDialog? gitClientDialog = null,
        iTerminal.Core.Dialogs.Documents.DocumentManagerDialog? documentManagerDialog = null,
        NetworkBrowserDialog? networkBrowserDialog = null,
        iTerminal.Core.Dialogs.PackageManager.PackageManagerDialog? packageManagerDialog = null,
        iTerminal.Core.Dialogs.Portfolio.PortfolioManagerDialog? portfolioManagerDialog = null,
        iTerminal.Core.Dialogs.Portfolio.PortfolioSetupWizardDialog? portfolioSetupWizardDialog = null,
        iTerminal.Core.Dialogs.Calculator.CalculatorDialog? calculatorDialog = null,
        iTerminal.Core.Dialogs.UnitConverter.UnitConverterDialog? unitConverterDialog = null,
        iTerminal.Core.Dialogs.Calendar.CalendarDialog? calendarDialog = null,
        iTerminal.Core.Dialogs.ToDo.ToDoListDialog? toDoListDialog = null,
        iTerminal.Core.Dialogs.Mail.MailDialog? mailDialog = null,
        iTerminal.Core.Dialogs.PasswordManager.PasswordManagerDialog? passwordManagerDialog = null,
        iTerminal.Core.Dialogs.Search.FindFilesDialog? findFilesDialog = null,
        MarkPatternDialog? markPatternDialog = null,
        MarkPatternDialog? unmarkPatternDialog = null,
        CompressDialog? compressDialog = null,
        ExtractDialog? extractDialog = null,
        CommandPaletteDialog? commandPaletteDialog = null,
        CompareFoldersDialog? compareFoldersDialog = null,
        iTerminal.Core.Commands.InteractiveShellCommand? shellCommand = null,
        iTerminal.Core.Dialogs.RuntimeDependencyDialog? runtimeDependencyDialog = null,
        FileDiffDialog? fileDiffDialog = null,
        iTerminal.Core.Services.Clipboard.FileClipboardOperations? clipboardOperations = null,
        DiskUsageDialog? diskUsageDialog = null,
        iTerminal.Core.Services.Elevation.IElevationService? elevationService = null,
        iConsole.Services.INotificationService? notificationService = null,
        SecureWipeDialog? secureWipeDialog = null,
        SignatureDialog? signatureDialog = null,
        DuplicateFinderDialog? duplicateFinderDialog = null,
        iTerminal.Core.Services.Defender.IMalwareScanService? malwareScanService = null,
        SystemSettingsDialog? systemSettingsDialog = null,
        iTerminal.Core.Dialogs.Web.WebBrowserDialog? webBrowserDialog = null,
        iTerminal.Core.Dialogs.Stocks.StockBrowserDialog? stockBrowserDialog = null
        )
        : base([], themeService)
    {
        _settingsService = settingsService;

        Name = nameof(iConsole.Controls.MenuBar);

        AddMenuItem(new FileMenuItem(
            contentModelService,
            fileViewerDialog,
            textFileEditorDialog,
            createFolderDialog,
            createFileDialog,
            renameDialog,
            themeService,
            musicFileViewerDialog: musicFileViewerDialog,
            videoFileViewerDialog: videoFileViewerDialog,
            imageFileViewerDialog: imageFileViewerDialog,
            findFilesDialog: findFilesDialog,
            compressDialog: compressDialog,
            extractDialog: extractDialog,
            secureWipeDialog: secureWipeDialog,
            signatureDialog: signatureDialog,
            malwareScanService: malwareScanService,
            notificationService: notificationService));

        AddMenuItem(new EditMenuItem(
            contentModelService,
            copyDialog,
            moveDialog,
            renameDialog,
            deleteDialog,
            themeService,
            markPatternDialog,
            unmarkPatternDialog,
            compareFoldersDialog,
            fileDiffDialog,
            clipboardOperations));

        AddMenuItem(new NavigateMenuItem(
            contentModelService,
            folderHistoryService,
            driveInfoModel,
            folderIndexModel,
            beepService,
            dateTimeProviderService,
            settingsService,
            frequentExecutionGuardService,
            content,
            themeService,
            helpDialog,
            shellCommand));

        AddMenuItem(new ToolsMenuItem(
            terminalDialog,
            diskManagerDialog,
            networkManagerDialog,
            iPhoneBrowserDialog,
            contentModelService,
            themeManagerDialog,
            settingsDialog,
            taskManagerDialog,
            processManagerDialog,
            systemInfoDialog,
            themeService,
            gitClientDialog,
            documentManagerDialog,
            networkBrowserDialog,
            packageManagerDialog,
            portfolioManagerDialog,
            portfolioSetupWizardDialog,
            calculatorDialog,
            unitConverterDialog,
            calendarDialog,
            toDoListDialog,
            mailDialog,
            passwordManagerDialog,
            settingsService,
            runtimeDependencyDialog,
            diskUsageDialog,
            elevationService,
            notificationService,
            duplicateFinderDialog,
            systemSettingsDialog,
            webBrowserDialog,
            stockBrowserDialog));

        AddMenuItem(new AboutMenuItem(helpDialog, aboutDialog, themeService, commandPaletteDialog));

        _statusBarTopRight = new iConsole.Controls.StatusBar.StatusBar(themeService);
        _statusBarTopRight.AddControl(new Date(themeService, dateTimeProviderService));
        AddControl(_statusBarTopRight);

        _taskStatus = new iConsole.Controls.StatusBar.TaskStatus(taskManager, themeService);
        AddControl(_taskStatus);

        _weather = new WeatherIndicator(weatherService, themeService);
        AddControl(_weather);

        _quoteBTC = new QuoteIndicator("Bitcoin", onlineQuoteService, themeService);
        AddControl(_quoteBTC);

        _quoteETH = new QuoteIndicator("Ethereum", onlineQuoteService, themeService);
        AddControl(_quoteETH);
    }

    public override Task OnInitializeAsync()
    {
        ReadIndicatorSettings();
        return base.OnInitializeAsync();
    }

    /// <summary>Reads the two indicator switches; true when either changed.</summary>
    private bool ReadIndicatorSettings()
    {
        var changed = false;

        bool? isQuotesIndicatorEnabled = _settingsService.GetAsBool(Settings.IsQuotesIndicatorEnabled, true);
        if (isQuotesIndicatorEnabled is not null && isQuotesIndicatorEnabled.Value != _isQuotesIndicatorEnabled)
        {
            _isQuotesIndicatorEnabled = isQuotesIndicatorEnabled.Value;
            changed = true;
        }

        bool? isWeatherIndicatorEnabled = _settingsService.GetAsBool(Settings.IsWeatherIndicatorEnabled, true);
        if (isWeatherIndicatorEnabled is not null && isWeatherIndicatorEnabled.Value != _isWeatherIndicatorEnabled)
        {
            _isWeatherIndicatorEnabled = isWeatherIndicatorEnabled.Value;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Picks up the indicator switches as they are changed in the settings dialog. They were read
    /// once, at start-up, so turning an indicator off did nothing until the app was restarted.
    /// <see cref="ISettingsService"/> has no change notification, and reading it is a lookup in its
    /// in-memory cache, so polling here costs nothing.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();
        if (ReadIndicatorSettings())
        {
            OnInitializeLayout();
            GetRootControl()?.Refresh();
        }
    }

    internal bool IsQuoteIndicatorShownForTest => _quoteBTC.IsVisible && _quoteETH.IsVisible;
    internal bool IsWeatherIndicatorShownForTest => _weather.IsVisible;

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        long totalWidth = ViewSize.Width;
        long dateWidth = Math.Min(DateTimeWidth, totalWidth);
        long preferredMenuWidth = MenuItems.Any()
            ? MenuItems.Sum(item => item.Text.Length) + (MenuItems.Count - 1)
            : 1;

        long remainingForOptionalIndicators = totalWidth - dateWidth - preferredMenuWidth;
        long rightCursor = totalWidth - dateWidth;

        _statusBarTopRight.IsVisible = true;
        _statusBarTopRight.Position = new Position(Math.Max(totalWidth - dateWidth, 0), 0);
        _statusBarTopRight.ViewSize = new Size(dateWidth, 1);

        void PlaceIndicator(iConsole.Controls.Control indicator, long width)
        {
            rightCursor -= width;
            indicator.IsVisible = true;
            indicator.Position = new Position(Math.Max(rightCursor, 0), 0);
            indicator.ViewSize = new Size(width, 1);
            remainingForOptionalIndicators -= width;
        }

        if (remainingForOptionalIndicators >= TaskIndicatorWidth)
        {
            PlaceIndicator(_taskStatus, TaskIndicatorWidth);
        }
        else
        {
            _taskStatus.IsVisible = false;
        }

        long quotesWidth = QuoteEthIndicatorWidth + QuoteBtcIndicatorWidth;
        if (_isQuotesIndicatorEnabled && remainingForOptionalIndicators >= quotesWidth)
        {
            PlaceIndicator(_quoteETH, QuoteEthIndicatorWidth);
            PlaceIndicator(_quoteBTC, QuoteBtcIndicatorWidth);
        }
        else
        {
            _quoteETH.IsVisible = false;
            _quoteBTC.IsVisible = false;
        }

        if (_isWeatherIndicatorEnabled && remainingForOptionalIndicators >= WeatherIndicatorWidth)
        {
            PlaceIndicator(_weather, WeatherIndicatorWidth);
        }
        else
        {
            _weather.IsVisible = false;
        }

        ViewSize.Width = Math.Max(rightCursor, 1);
    }
}

