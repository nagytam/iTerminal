using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Data;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Music;
using iTerminal.Core.Services.Remote;

namespace iTerminal.Core.Controls.MenuBar;

public class TerminalMenuBar : iConsole.Controls.MenuBar
{
    private const int DateTimeWidth = 19;
    private const int TaskIndicatorWidth = 10;
    private const int WeatherIndicatorWidth = 20;
    private const int QuoteBtcIndicatorWidth = 18;
    private const int QuoteEthIndicatorWidth = 17;
    private const int OnlineIndicatorWidth = 10;

    private ISettingsService _settingsService { get; set; }
    private iConsole.Controls.StatusBar.StatusBar _statusBarTopRight { get; set; }
    private iConsole.Controls.StatusBar.TaskStatus _taskStatus { get; set; }
    private WeatherIndicator _weather { get; set; }
    private QuoteIndicator _quoteETH { get; set; }
    private QuoteIndicator _quoteBTC { get; set; }
    private OnlineIndicator _online { get; set; }
    private MusicIndicator? _music { get; set; }
    private IMusicPlayerService? _musicPlayer { get; set; }

    /// <summary>Whether music or radio was playing at the last layout or idle pass; a change lays the bar out again.</summary>
    private bool _isMusicPlaying;

    private bool _isWeatherIndicatorEnabled = true;
    private bool _isOnlineIndicatorEnabled = true;
    private readonly ILocalizer? _localizer;
    private volatile bool _languageChanged;
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
        INetworkDiscoveryService networkDiscoveryService,
        INetworkProfileService networkProfileService,
        IRemoteConnectionService remoteConnectionService,
        IPasswordManager passwordManager,
        IPromptService promptService,
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
        iConsole.Dialogs.XmlViewerDialog? xmlViewerDialog = null,
        iConsole.Dialogs.JsonViewerDialog? jsonViewerDialog = null,
        iConsole.Dialogs.CsvViewerDialog? csvViewerDialog = null,
        // Tools, the larger features (optional so the menu can be built without them)
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
        iTerminal.Core.Dialogs.Search.FindFilesDialog? findFilesDialog = null,
        MarkPatternDialog? markPatternDialog = null,
        MarkPatternDialog? unmarkPatternDialog = null,
        CompressDialog? compressDialog = null,
        ExtractDialog? extractDialog = null,
        CommandPaletteDialog? commandPaletteDialog = null,
        CompareFoldersDialog? compareFoldersDialog = null,
        SynchronizeFoldersDialog? synchronizeFoldersDialog = null,
        StripMetadataDialog? stripMetadataDialog = null,
        FileLabelsDialog? fileLabelsDialog = null,
        LabelFilterDialog? labelFilterDialog = null,
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
        iTerminal.Core.Dialogs.Stocks.StockBrowserDialog? stockBrowserDialog = null,
        iTerminal.Core.Dialogs.Terminal.MarketTerminalDialog? marketTerminalDialog = null,
        iTerminal.Core.Dialogs.Map.MapDialog? mapDialog = null,
        iTerminal.Core.Dialogs.Remote.RemoteConnectionManagerDialog? remoteConnectionManagerDialog = null,
        iTerminal.Core.Dialogs.Translator.TranslatorDialog? translatorDialog = null,
        iTerminal.Core.Dialogs.CurrencyConverter.CurrencyConverterDialog? currencyConverterDialog = null,
        iTerminal.Core.Dialogs.Workspaces.WorkspaceManagerDialog? workspaceManagerDialog = null,
        LanguageDialog? languageDialog = null,
        ILocalizer? localizer = null,
        iTerminal.Core.Dialogs.Torrents.TorrentManagerDialog? torrentManagerDialog = null,
        iTerminal.Core.Dialogs.Contacts.ContactManagerDialog? contactManagerDialog = null,
        iTerminal.Core.Dialogs.Radio.OnlineRadioDialog? onlineRadioDialog = null,
        iTerminal.Core.Dialogs.EnvVars.EnvironmentVariablesDialog? environmentVariablesDialog = null,
        iTerminal.Core.Dialogs.Encyclopedia.EncyclopediaDialog? encyclopediaDialog = null,
        iTerminal.Core.Dialogs.YouTube.YouTubeDialog? youTubeDialog = null,
        iTerminal.Core.Dialogs.Benchmark.PerformanceBenchmarkDialog? performanceBenchmarkDialog = null,
        iTerminal.Core.Dialogs.Games.TicTacToeDialog? ticTacToeDialog = null,
        iTerminal.Core.Dialogs.Games.ChessDialog? chessDialog = null,
        iTerminal.Core.Dialogs.Games.GoDialog? goDialog = null,
        iTerminal.Core.Dialogs.Games.NineMensMorrisDialog? nineMensMorrisDialog = null,
        iTerminal.Core.Dialogs.Games.SnakeDialog? snakeDialog = null,
        iTerminal.Core.Dialogs.Games.TetrisDialog? tetrisDialog = null,
        iTerminal.Core.Dialogs.Games.RiskDialog? riskDialog = null,
        PrintDialog? printDialog = null,
        iTerminal.Core.Dialogs.Bluetooth.BluetoothDeviceManagerDialog? bluetoothDeviceManagerDialog = null,
        VirtualMachineManagerDialog? virtualMachineManagerDialog = null,
        MultiRenameDialog? multiRenameDialog = null,
        iTerminal.Core.Services.RenameUndoService? renameUndoService = null,
        HashManifestDialog? hashManifestDialog = null,
        iTerminal.Core.Services.FileSystemResolver? fileSystemResolver = null,
        iConsole.Dialogs.HdfViewerDialog? hdfViewerDialog = null,
        StreamsDialog? streamsDialog = null,
        iConsole.Dialogs.ExecutableAnalysisDialog? executableAnalysisDialog = null,
        FileEncryptionDialog? fileEncryptionDialog = null,
        iTerminal.Core.Dialogs.Games.JassDialog? jassDialog = null,
        iTerminal.Core.Dialogs.UsersAndGroups.UserGroupManagerDialog? userGroupManagerDialog = null,
        iTerminal.Core.Dialogs.Scanning.ScannerDialog? scannerDialog = null,
        iTerminal.Core.Dialogs.NetworkTools.NetworkToolsDialog? networkToolsDialog = null,
        iTerminal.Core.Dialogs.Plugins.PluginManagerDialog? pluginManagerDialog = null,
        iConsole.Dialogs.XpsViewerDialog? xpsViewerDialog = null,
        iConsole.Dialogs.CertificateViewerDialog? certificateViewerDialog = null,
        iConsole.Dialogs.HarViewerDialog? harViewerDialog = null,
        iConsole.Dialogs.ParquetViewerDialog? parquetViewerDialog = null,
        iConsole.Dialogs.MarkdownViewerDialog? markdownViewerDialog = null,
        iConsole.Dialogs.YamlViewerDialog? yamlViewerDialog = null,
        iConsole.Dialogs.TomlViewerDialog? tomlViewerDialog = null,
        iConsole.Dialogs.DbfViewerDialog? dbfViewerDialog = null,
        iTerminal.Core.Jobs.UpdateCheckJob? updateCheckJob = null,
        iConsole.Viewers.FileViewerRegistry? fileViewers = null,
        iConsole.Viewers.FileEditorRegistry? fileEditors = null,
        iTerminal.Core.Dialogs.Weather.WeatherDialog? weatherDialog = null,
        InternetConnectivityService? connectivityService = null,
        IMusicPlayerService? musicPlayerService = null,
        iTerminal.Core.Dialogs.FileTransfer.FileTransferDialog? fileTransferDialog = null
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
            webBrowserDialog: webBrowserDialog,
            xmlViewerDialog: xmlViewerDialog,
            jsonViewerDialog: jsonViewerDialog,
            csvViewerDialog: csvViewerDialog,
            findFilesDialog: findFilesDialog,
            compressDialog: compressDialog,
            extractDialog: extractDialog,
            secureWipeDialog: secureWipeDialog,
            fileEncryptionDialog: fileEncryptionDialog,
            signatureDialog: signatureDialog,
            malwareScanService: malwareScanService,
            notificationService: notificationService,
            printDialog: printDialog,
            hashManifestDialog: hashManifestDialog,
            fileSystemResolver: fileSystemResolver,
            hdfViewerDialog: hdfViewerDialog,
            streamsDialog: streamsDialog,
            executableAnalysisDialog: executableAnalysisDialog,
            xpsViewerDialog: xpsViewerDialog,
            certificateViewerDialog: certificateViewerDialog,
            harViewerDialog: harViewerDialog,
            parquetViewerDialog: parquetViewerDialog,
            markdownViewerDialog: markdownViewerDialog,
            yamlViewerDialog: yamlViewerDialog,
            tomlViewerDialog: tomlViewerDialog,
            dbfViewerDialog: dbfViewerDialog,
            fileViewers: fileViewers,
            fileEditors: fileEditors));

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
            synchronizeFoldersDialog,
            stripMetadataDialog,
            fileLabelsDialog,
            labelFilterDialog,
            clipboardOperations,
            multiRenameDialog: multiRenameDialog,
            renameUndoService: renameUndoService,
            notificationService: notificationService));

        AddMenuItem(new NavigateMenuItem(
            contentModelService,
            folderHistoryService,
            driveInfoModel,
            folderIndexModel,
            beepService,
            dateTimeProviderService,
            settingsService,
            frequentExecutionGuardService,
            networkDiscoveryService,
            networkProfileService,
            remoteConnectionService,
            passwordManager,
            promptService,
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
            stockBrowserDialog,
            marketTerminalDialog,
            mapDialog,
            remoteConnectionManagerDialog,
            translatorDialog,
            currencyConverterDialog,
            workspaceManagerDialog,
            languageDialog,
            torrentManagerDialog,
            contactManagerDialog,
            onlineRadioDialog,
            environmentVariablesDialog,
            encyclopediaDialog,
            youTubeDialog,
            performanceBenchmarkDialog,
            ticTacToeDialog,
            chessDialog,
            goDialog,
            nineMensMorrisDialog,
            snakeDialog,
            tetrisDialog,
            riskDialog: riskDialog,
            bluetoothDeviceManagerDialog: bluetoothDeviceManagerDialog,
            virtualMachineManagerDialog: virtualMachineManagerDialog,
            jassDialog: jassDialog,
            userGroupManagerDialog: userGroupManagerDialog,
            scannerDialog: scannerDialog,
            networkToolsDialog: networkToolsDialog,
            weatherDialog: weatherDialog,
            pluginManagerDialog: pluginManagerDialog,
            fileTransferDialog: fileTransferDialog));

        AddMenuItem(new AboutMenuItem(helpDialog, aboutDialog, themeService, commandPaletteDialog, updateCheckJob));

        _localizer = localizer;
        if (localizer is not null)
        {
            // The language can change on any thread; the idle pass re-reads the labels on the UI thread.
            localizer.LanguageChanged += (_, _) => _languageChanged = true;
            LocalizeMenus();
        }

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

        _online = new OnlineIndicator(connectivityService ?? new InternetConnectivityService(), themeService);
        AddControl(_online);

        if (musicPlayerService is not null)
        {
            _musicPlayer = musicPlayerService;
            // Hidden until playback starts: the bar shows it from layout, not on its own.
            _music = new MusicIndicator(musicPlayerService, taskManager, themeService) { IsVisible = false };
            AddControl(_music);
        }
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

        bool? isOnlineIndicatorEnabled = _settingsService.GetAsBool(Settings.IsOnlineIndicatorEnabled, true);
        if (isOnlineIndicatorEnabled is not null && isOnlineIndicatorEnabled.Value != _isOnlineIndicatorEnabled)
        {
            _isOnlineIndicatorEnabled = isOnlineIndicatorEnabled.Value;
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
        bool languageChanged = _languageChanged;
        if (languageChanged)
        {
            _languageChanged = false;
            LocalizeMenus();
        }

        // The widget and the budget depend on whether anything plays, so a change re-lays the bar out at once.
        bool musicChanged = IsMusicPlayingChanged();

        if (ReadIndicatorSettings() || languageChanged || musicChanged)
        {
            OnInitializeLayout();
            GetRootControl()?.Refresh();
        }
    }

    /// <summary>True when playback started or stopped since the last call. The player has no event the bar can use from its pump thread, so this polls on the UI thread.</summary>
    private bool IsMusicPlayingChanged()
    {
        if (_musicPlayer is null)
        {
            return false;
        }

        bool isPlaying = _musicPlayer.State != PlaybackState.Stopped;
        if (isPlaying == _isMusicPlaying)
        {
            return false;
        }

        _isMusicPlaying = isPlaying;
        return true;
    }

    /// <summary>Shows every menu and entry in the localizer's language; the menu titles change width, so the bar lays out again.</summary>
    internal void LocalizeMenus()
    {
        if (_localizer is null)
        {
            return;
        }

        foreach (iConsole.Controls.MenuItem menuItem in MenuItems)
        {
            menuItem.Localize(_localizer);
        }
    }

    internal bool IsQuoteIndicatorShownForTest => _quoteBTC.IsVisible && _quoteETH.IsVisible;
    internal bool IsWeatherIndicatorShownForTest => _weather.IsVisible;
    internal bool IsOnlineIndicatorShownForTest => _online.IsVisible;
    internal bool IsMusicIndicatorShownForTest => _music?.IsVisible ?? false;

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

        // Next to the clock, so the connection state reads with the time.
        if (_isOnlineIndicatorEnabled && remainingForOptionalIndicators >= OnlineIndicatorWidth)
        {
            PlaceIndicator(_online, OnlineIndicatorWidth);
        }
        else
        {
            _online.IsVisible = false;
        }

        if (remainingForOptionalIndicators >= TaskIndicatorWidth)
        {
            PlaceIndicator(_taskStatus, TaskIndicatorWidth);
        }
        else
        {
            _taskStatus.IsVisible = false;
        }

        // Placed before Quotes and Weather, so when the bar is short those two give way first. Reserved only
        // while something plays: a Stopped player leaves its room to the quotes and weather.
        if (_music is not null)
        {
            if (_musicPlayer is not null)
            {
                _isMusicPlaying = _musicPlayer.State != PlaybackState.Stopped;
            }

            if (_isMusicPlaying && remainingForOptionalIndicators >= MusicIndicator.Width)
            {
                PlaceIndicator(_music, MusicIndicator.Width);
            }
            else
            {
                _music.IsVisible = false;
            }
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
