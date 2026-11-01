using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Jobs;
using iConsole.Services;
using iTerminal.Core.AI;
using iTerminal.Core.Audio;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Controls.Lenses;
using iTerminal.Core.Controls.MenuBar;
using iTerminal.Core.Controls.StatusBar;
using iTerminal.Core.Controls.Terminals;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Jobs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Labels;
using iTerminal.Core.Services.RuntimeDependencies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using iTerminal.Core.Tasks;

namespace iTerminal.Core.Application;

public static class iTerminalApplicationBuilderExtension
{
    /// <summary>
    /// Configuration key naming the folder that holds the database. Unset in production, where the
    /// database lives under <c>%APPDATA%/iTerminal</c>; the end-to-end tests point it at a temporary
    /// folder so a test run never reads or rewrites the user's own settings.
    /// </summary>
    public const string DataDirectoryKey = "DataDirectory";

    /// <summary>
    /// The folder the app's own data folder goes under: <c>%APPDATA%</c> on Windows,
    /// <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> elsewhere — created if it is not there. Asked for
    /// without that, .NET answers with an <em>empty string</em> on a Linux account that has no
    /// <c>~/.config</c> yet, and "iTerminal" under an empty string is a folder in whatever
    /// directory the app was started from: the database, the vault keys and the playlists landed
    /// in the user's working directory, a new set in every folder the app was run in.
    /// </summary>
    public static string UserDataRoot() =>
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);

    public static IServiceCollection AddiTerminal(this IServiceCollection services, IConfiguration? configuration = null)
    {
        string databaseDirectory = configuration?[DataDirectoryKey] is { Length: > 0 } configuredDirectory
            ? configuredDirectory
            : Path.Combine(UserDataRoot(), "iTerminal");
        Directory.CreateDirectory(databaseDirectory);

        string databasePath = Path.Combine(databaseDirectory, "iTerminal.sqlite");

        // Bind FileInformationRefreshOptions from configuration if available.
        // This is optional; consumers may register configuration under
        // "FileInformationRefresh" to override defaults.
        if (configuration is not null)
        {
            services.Configure<FileInformationRefreshOptions>(configuration.GetSection("FileInformationRefresh"));
            services.Configure<AiOptions>(configuration.GetSection("AI"));
            services.Configure<iTerminal.Core.Services.Cloud.CloudOptions>(configuration.GetSection("Cloud"));
            services.Configure<iTerminal.Core.Services.Translation.TranslationOptions>(configuration.GetSection("Translation"));
        }

        services.AddOptions<iTerminal.Core.Services.Translation.TranslationOptions>();
        services.AddOptions<FileInformationRefreshOptions>();
        services.AddOptions<AiOptions>();
        services.AddOptions<iTerminal.Core.Services.Cloud.CloudOptions>();

        services.AddSingleton<IChatClient>(sp =>
        {
            var aiOptions = sp.GetRequiredService<IOptions<AiOptions>>().Value;
            string? apiKey = null;
            if (aiOptions.Provider != AiProvider.None)
            {
                apiKey = Environment.GetEnvironmentVariable(aiOptions.ApiKeyEnvVar);
                if (string.IsNullOrEmpty(apiKey))
                    apiKey = sp.GetRequiredService<IPasswordManager>().GetSecret(AiOptions.ApiKeyPasswordId);
            }

            return AiChatClientFactory.Create(aiOptions, apiKey);
        });

        services.AddSingleton<AiChatModel>();
        services.AddSingleton<IUiService, UiService>();
        services.AddSingleton<IAiChatService, AiChatService>();
        services.AddTransient<AiModelDialog>();
        services.AddTransient<AiPromptBar>();

        services.AddSingleton<StartupWarningService>();

        // Ctrl+Shift+F3: the language of the interface. The strings are .resx files; the choice is a setting.
        services.AddSingleton<iConsole.Localization.ILocalizationSource>(_ => new iConsole.Localization.ResourceLocalizationSource(
            new System.Resources.ResourceManager("iTerminal.Core.Localization.Resources.AppStrings", typeof(iTerminalApplicationBuilderExtension).Assembly)));
        services.AddSingleton<iConsole.Localization.ILanguageStore, iTerminal.Core.Services.SettingsLanguageStore>();
        services.AddTransient<LanguageDialog>();

        services.AddDbContextFactory<iTerminalDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<ICacheService, CacheService>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddTransient<TerminalMenuBar>();
        services.AddSingleton<Content>();
        services.AddSingleton<FilePanelFactory>();
        services.AddTransient<HotKeys>();
        services.AddTransient<ContentBrowser>();
        services.AddSingleton<iTerminal.Core.Services.Git.IGitService, iTerminal.Core.Services.Git.GitService>();
        services.AddSingleton<IContentLens, GitLens>();
        services.AddSingleton<IContentLens, FileLabelLens>();
        services.AddSingleton<IFileLabelService, FileLabelService>();
        services.AddTransient<iTerminal.Core.Dialogs.Git.GitClientDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.Calculator.CalculatorDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.UnitConverter.UnitConverterDialog>();
        services.AddSingleton<iTerminal.Core.Model.Calendar.MeetingStore>();
        services.AddTransient<iTerminal.Core.Dialogs.Calendar.CalendarDialog>();
        services.AddSingleton<iTerminal.Core.Model.ToDo.ToDoStore>();
        services.AddTransient<iTerminal.Core.Dialogs.ToDo.ToDoListDialog>();
        services.AddSingleton<iTerminal.Core.Model.Contacts.ContactStore>();
        services.AddTransient<iTerminal.Core.Dialogs.Contacts.ContactManagerDialog>();
        services.AddSingleton<iTerminal.Core.Model.Mail.MailAccountStore>();
        services.AddSingleton<iTerminal.Core.Services.Mail.IMailService, iTerminal.Core.Services.Mail.MailService>();
        services.AddTransient<iTerminal.Core.Dialogs.Mail.MailDialog>();
        services.AddSingleton<iTerminal.Core.Services.Web.IWebFetcher, iTerminal.Core.Services.Web.WebFetcher>();
        services.AddSingleton<iTerminal.Core.Services.Web.WebBrowserStore>();
        services.AddTransient<iTerminal.Core.Dialogs.Web.WebBrowserDialog>();
        // Alt+F10: the ASCII-art OpenStreetMap. Tiles are saved (and .mbtiles files read) under <data directory>/maps,
        // so it works offline; a test replaces the tile service so no run reaches the network.
        services.AddSingleton<iTerminal.Core.Services.Map.MapStore>();
        services.AddSingleton<iTerminal.Core.Services.Map.IMapSearchService, iTerminal.Core.Services.Map.MapSearchService>();
        services.AddSingleton<iTerminal.Core.Services.Map.IMapTileService>(provider => new iTerminal.Core.Services.Map.MapTileService(
            provider.GetRequiredService<iTerminal.Core.Services.Web.IWebFetcher>(),
            provider.GetRequiredService<iTerminal.Core.Services.Map.MapStore>(),
            Path.Combine(databaseDirectory, "maps")));
        services.AddTransient<iTerminal.Core.Dialogs.Map.MapDialog>();
        // Alt+F2: saved SSH and PowerShell remoting connections, each a keypress from a live session on the lent terminal.
        services.AddSingleton<iTerminal.Core.Services.Remote.IRemoteConnectionService, iTerminal.Core.Services.Remote.RemoteConnectionService>();
        services.AddTransient<iTerminal.Core.Dialogs.Remote.RemoteConnectionManagerDialog>();
        services.AddSingleton<iTerminal.Core.Services.Workspaces.IWorkspaceLayoutService, iTerminal.Core.Services.Workspaces.WorkspaceLayoutService>();
        services.AddSingleton<iTerminal.Core.Services.Workspaces.IWorkspaceService, iTerminal.Core.Services.Workspaces.WorkspaceService>();
        services.AddTransient<iTerminal.Core.Dialogs.Workspaces.WorkspaceManagerDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.PasswordManager.PasswordManagerDialog>();
        services.AddTransient<ItemInformation>();
        services.AddTransient<TerminalStatusBar>();
        services.AddTransient<ScreenManagerDialog>();
        services.AddTransient<TaskManagerDialog>();
        services.AddTransient<ProcessManagerDialog>();
        services.AddSingleton<ProcessService>();
        services.AddTransient<FileViewerDialog>();
        services.AddSingleton<iTerminal.Core.Services.Dotnet.IDotnetSolutionService, iTerminal.Core.Services.Dotnet.DotnetSolutionService>();
        services.AddTransient<iTerminal.Core.Dialogs.Dotnet.SolutionViewerDialog>();
        services.AddSingleton<iTerminal.Core.Services.PackageManager.IPackageManagerService>(_ =>
            OperatingSystem.IsWindows()
                ? new iTerminal.Core.Services.PackageManager.WinGetPackageManagerService()
                : new iTerminal.Core.Services.PackageManager.AptPackageManagerService());
        services.AddTransient<iTerminal.Core.Dialogs.PackageManager.PackageManagerDialog>();

        // Music player. Every output backend is registered; AudioDeviceService probes them at
        // runtime and keeps the first that works on this machine (see IAudioOutputBackend).
        services.AddSingleton<IAudioOutputBackend, iTerminal.Core.Audio.Sinks.WinMmAudioOutputBackend>();
        services.AddSingleton<IAudioOutputBackend, iTerminal.Core.Audio.Sinks.PulseAudioOutputBackend>();
        services.AddSingleton<IAudioOutputBackend, iTerminal.Core.Audio.Sinks.AplayAudioOutputBackend>();
        services.AddSingleton<iTerminal.Core.Services.Music.IAudioDeviceService, iTerminal.Core.Services.Music.AudioDeviceService>();
        services.AddSingleton<iTerminal.Core.Services.Music.IPlaylistService>(_ =>
            new iTerminal.Core.Services.Music.PlaylistService(Path.Combine(databaseDirectory, "playlists")));
        // The playing queue is session state, so it goes in the database next to the music index —
        // unlike a saved .m3u8, which is a document and stays a file.
        services.AddSingleton<iTerminal.Core.Services.Music.IMusicPlaylistStore, iTerminal.Core.Services.Music.MusicPlaylistStore>();
        // The player is a singleton so music keeps playing after the dialog closes; its pump thread
        // is owned by the service, not by any control.
        services.AddSingleton<iTerminal.Core.Services.Music.IMusicPlayerService>(serviceProvider =>
            new iTerminal.Core.Services.Music.MusicPlayerService(
                serviceProvider.GetRequiredService<iTerminal.Core.Services.Music.IAudioDeviceService>(),
                serviceProvider.GetService<Serilog.ILogger>()));
        // Restoring the saved session is a singleton of its own because the playlist panel may be
        // built before the jobs start, and then it — not the job — is what has to put the queue back.
        services.AddSingleton<iTerminal.Core.Services.Music.IMusicSessionRestoreService, iTerminal.Core.Services.Music.MusicSessionRestoreService>();
        services.AddSingleton<IJob, MusicSessionJob>();
        // Tag/format reading for the F3 music viewer. Stateless, so a singleton costs nothing.
        services.AddSingleton<iTerminal.Core.Services.Music.IMusicMetadataService, iTerminal.Core.Services.Music.MusicMetadataService>();
        services.AddTransient<iTerminal.Core.Dialogs.Music.MusicFileViewerDialog>();
        // F3 on a video or a picture. Both decode through ffmpeg; the factory holds no state.
        services.AddSingleton<iTerminal.Core.Video.FfmpegVideoDecoderFactory>();
        services.AddSingleton<iTerminal.Core.Video.IVideoDecoderFactory, iTerminal.Core.Video.PictureVideoDecoderFactory>();
        services.AddTransient<iTerminal.Core.Dialogs.Video.VideoFileViewerDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.Video.ImageFileViewerDialog>();
        services.AddTransient<XmlViewerDialog>();
        services.AddTransient<JsonViewerDialog>();
        services.AddTransient<iConsole.Dialogs.CsvViewerDialog>();
        // The SQLite viewer reads a database through the read-only reader; the reader holds no state.
        services.AddTransient<iConsole.Sqlite.ISqliteDatabaseReader, iTerminal.Core.Services.Sqlite.SqliteDatabaseReader>();
        services.AddTransient<iConsole.Dialogs.SqliteViewerDialog>();
        // The library index is a singleton so the in-memory copy of the table is shared by the
        // indexing job and every dialog that searches it.
        services.AddSingleton<iTerminal.Core.Services.Music.IMusicLibraryService, iTerminal.Core.Services.Music.MusicLibraryService>();
        services.AddSingleton<IJob, MusicLibraryIndexJob>();

        // Document index. The OCR engine is registered behind the settings gate so the extractors
        // only ever have to ask "can this run", never "does the user want it to".
        services.AddSingleton<iTerminal.Core.Documents.IOcrEngine>(serviceProvider =>
            new iTerminal.Core.Documents.SettingsGatedOcrEngine(
                new iTerminal.Core.Documents.TesseractOcrEngine(),
                serviceProvider.GetRequiredService<ISettingsService>()));
        // Built explicitly rather than by constructor selection: the factory has two constructors
        // and only this one — the production set of extractors — is meant for the container.
        services.AddSingleton(serviceProvider =>
            new iTerminal.Core.Documents.DocumentTextExtractorFactory(
                serviceProvider.GetRequiredService<iTerminal.Core.Documents.IOcrEngine>()));
        // A singleton for the same reason the music library is one: the in-memory copy of the
        // metadata table is shared by the indexing job and the dialog that searches it.
        services.AddSingleton<iTerminal.Core.Services.Documents.IDocumentLibraryService>(serviceProvider =>
            new iTerminal.Core.Services.Documents.DocumentLibraryService(
                serviceProvider.GetRequiredService<IDbContextFactory<iTerminalDbContext>>(),
                serviceProvider.GetRequiredService<ISettingsService>(),
                serviceProvider.GetRequiredService<iTerminal.Core.Documents.IOcrEngine>(),
                serviceProvider.GetRequiredService<iTerminal.Core.Documents.DocumentTextExtractorFactory>(),
                serviceProvider.GetService<Serilog.ILogger>(),
                serviceProvider.GetRequiredService<iTerminal.Core.Services.BackgroundStart.IBackgroundStartService>()));
        services.AddSingleton<IJob, DocumentLibraryIndexJob>();
        services.AddTransient<iTerminal.Core.Dialogs.Documents.DocumentManagerDialog>();
        services.AddTransient<TextFileEditorDialog>();
        services.AddTransient<CreateFolderDialog>();
        services.AddTransient<CreateFileDialog>();
        services.AddTransient<DeleteDialog>();
        services.AddTransient<RenameDialog>();
        services.AddTransient<MarkPatternDialog>();
        services.AddTransient<DiskManagerDialog>();
        services.AddTransient<DiskUsageDialog>();
        services.AddTransient<DuplicateFinderDialog>();
        services.AddSingleton<iTerminal.Core.Services.Duplicates.IDuplicateFinderService, iTerminal.Core.Services.Duplicates.DuplicateFinderService>();
        services.AddSingleton<iTerminal.Core.Services.DiskUsage.IDiskUsageService, iTerminal.Core.Services.DiskUsage.DiskUsageService>();
        services.AddSingleton<iTerminal.Core.Services.Elevation.IElevationService, iTerminal.Core.Services.Elevation.ElevationService>();
        services.AddTransient<FormatDriveDialog>();
        services.AddTransient<DiskImageDialog>();
        services.AddSingleton<iTerminal.Core.Services.Drives.IDriveActionsService, iTerminal.Core.Services.Drives.DriveActionsService>();
        services.AddTransient<ThemeManagerDialog>();
        services.AddTransient<SettingsDialog>();
        services.AddSingleton<FolderHistoryService>();
        services.AddSingleton<IContentModelService, ContentModelService>();

        // The tool-backed drives (docker://, git://), the F7 search behind search:// and the
        // quick-view panel behind preview://. Singletons: each holds state the panels share —
        // the remembered repositories, the last search's results.
        services.AddSingleton<iTerminal.Core.Services.Processes.IExternalProcessRunner, iTerminal.Core.Services.Processes.ExternalProcessRunner>();
        services.AddSingleton<iTerminal.Core.Services.Git.IGitDriveService, iTerminal.Core.Services.Git.GitDriveService>();
        services.AddSingleton<iTerminal.Core.Services.Docker.IDockerDriveService, iTerminal.Core.Services.Docker.DockerDriveService>();
        services.AddSingleton<iTerminal.Core.Services.Search.IFileSearchService, iTerminal.Core.Services.Search.FileSearchService>();
        services.AddSingleton<iTerminal.Core.Services.Archives.IArchiveCompressionService, iTerminal.Core.Services.Archives.ArchiveCompressionService>();
        services.AddTransient<CompressDialog>();
        services.AddSingleton<iTerminal.Core.Services.Archives.IArchiveExtractionService, iTerminal.Core.Services.Archives.ArchiveExtractionService>();
        services.AddTransient<ExtractDialog>();
        services.AddTransient<CommandPaletteDialog>();
        // Ctrl+L: symbolic and hard links on the local disk.
        services.AddSingleton<iTerminal.Core.Services.Links.ILinkService, iTerminal.Core.Services.Links.LinkService>();
        services.AddTransient<CreateLinkDialog>();
        services.AddTransient<EditLinkDialog>();
        // Shift+F11: the order of a panel's rows.
        services.AddTransient<SortDialog>();
        services.AddSingleton<iTerminal.Core.Services.UserMenu.IUserMenuService>(sp =>
            new iTerminal.Core.Services.UserMenu.UserMenuService(
                Path.Combine(databaseDirectory, iTerminal.Core.Services.UserMenu.UserMenuService.FileName),
                sp.GetRequiredService<iTerminal.Core.Services.Shell.IInteractiveShellService>(),
                sp.GetRequiredService<iConsole.Interfaces.IContentModelService>()));
        services.AddTransient<UserMenuDialog>();
        // Ctrl+F8: the recycle bin as a drive. The store is the operating system's way of keeping
        // deleted files; a test registers one built in a folder of its own.
        services.AddSingleton<iTerminal.Core.Services.Recycle.IRecycleBinStore>(_ => iTerminal.Core.Services.Recycle.RecycleBinService.CreateStore());
        services.AddSingleton<iTerminal.Core.Services.Recycle.IRecycleBinService, iTerminal.Core.Services.Recycle.RecycleBinService>();
        // Alt+F7: the full-screen stock browser. Quotes come from Yahoo Finance; a test replaces the
        // provider so no run reaches the network.
        services.AddSingleton<iTerminal.Core.Services.Stocks.IStockQuoteProvider, iTerminal.Core.Services.Stocks.YahooStockQuoteProvider>();
        services.AddSingleton<iTerminal.Core.Services.Stocks.IStockService, iTerminal.Core.Services.Stocks.StockService>();
        services.AddTransient<iTerminal.Core.Dialogs.Stocks.StockBrowserDialog>();
        // Ctrl+Shift+F4: the translator. A LibreTranslate-compatible API (Translation:ApiUrl) and the connected AI
        // model are two providers behind one service; a test replaces the providers so no run reaches the network.
        services.AddSingleton<iTerminal.Core.Services.Translation.ILanguageDetector, iTerminal.Core.Services.Translation.LanguageDetector>();
        services.AddSingleton<iTerminal.Core.Services.Translation.ITranslationProvider>(provider =>
        {
            var translation = provider.GetRequiredService<IOptions<iTerminal.Core.Services.Translation.TranslationOptions>>().Value;
            return new iTerminal.Core.Services.Translation.LibreTranslateProvider(translation.ApiUrl, () =>
            {
                string? key = Environment.GetEnvironmentVariable(translation.ApiKeyEnvVar);
                if (!string.IsNullOrEmpty(key))
                {
                    return key;
                }

                try
                {
                    return provider.GetRequiredService<IPasswordManager>().GetSecret(iTerminal.Core.Services.Translation.TranslationOptions.ApiKeyPasswordId);
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    return null;
                }
            });
        });
        services.AddSingleton<iTerminal.Core.Services.Translation.ITranslationProvider, iTerminal.Core.Services.Translation.LlmTranslationProvider>();
        services.AddSingleton<iTerminal.Core.Services.Translation.ITranslationService, iTerminal.Core.Services.Translation.TranslationService>();
        services.AddTransient<iTerminal.Core.Dialogs.Translator.TranslatorDialog>();
        // Ctrl+Shift+F5: the currency converter. Rates are the European Central Bank's daily file; a test replaces
        // the provider so no run reaches the network.
        services.AddSingleton<iTerminal.Core.Services.Currency.ICurrencyRateProvider, iTerminal.Core.Services.Currency.EcbCurrencyRateProvider>();
        services.AddSingleton<iTerminal.Core.Services.Currency.ICurrencyService, iTerminal.Core.Services.Currency.CurrencyService>();
        services.AddTransient<iTerminal.Core.Dialogs.CurrencyConverter.CurrencyConverterDialog>();
        // Ctrl+Shift+W: the encyclopedia. Wikipedia and Wiktionary are read through the MediaWiki API; a test replaces the
        // provider so no run reaches the network.
        services.AddSingleton<iTerminal.Core.Services.Encyclopedia.IEncyclopediaProvider, iTerminal.Core.Services.Encyclopedia.WikiEncyclopediaProvider>();
        services.AddSingleton<iTerminal.Core.Services.Encyclopedia.IEncyclopediaService, iTerminal.Core.Services.Encyclopedia.EncyclopediaService>();
        services.AddTransient<iTerminal.Core.Dialogs.Encyclopedia.EncyclopediaDialog>();
        // Ctrl+Shift+B: the performance benchmark. Its probes are registered in the order a run measures them; each one only reads or computes.
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.CpuSingleThreadProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.CpuMultiThreadProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.MemoryCopyProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.MemoryAllocationProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.DiskSequentialReadProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.DiskRandomReadProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.DiskListingProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.NetworkLoopbackProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.NetworkLatencyProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.NetworkDownloadProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IBenchmarkProbe, iTerminal.Core.Services.Benchmark.TimerWakeupProbe>();
        services.AddSingleton<iTerminal.Core.Services.Benchmark.IPerformanceBenchmarkService, iTerminal.Core.Services.Benchmark.PerformanceBenchmarkService>();
        services.AddTransient<iTerminal.Core.Dialogs.Benchmark.PerformanceBenchmarkDialog>();
        // Ctrl+Shift+F1: the BitTorrent manager. The engine starts on first use, so a run that never adds a torrent opens no sockets.
        services.AddSingleton<iTerminal.Core.Services.Torrents.ITorrentService>(provider => new iTerminal.Core.Services.Torrents.TorrentService(
            provider.GetRequiredService<ISettingsService>(), databaseDirectory));
        services.AddTransient<iTerminal.Core.Dialogs.Torrents.TorrentManagerDialog>();
        // Tools > Games: Tic-Tac-Toe, Chess, Go and Nine Men's Morris. The dialog is a transient that keeps the games' positions for the run.
        services.AddTransient<iTerminal.Core.Dialogs.Games.GamesDialog>();
        // Ctrl+F12 properties, Shift+F12 checksums.
        services.AddSingleton<iTerminal.Core.Services.Properties.IFilePropertiesService, iTerminal.Core.Services.Properties.FilePropertiesService>();
        services.AddSingleton<iTerminal.Core.Services.Checksums.IChecksumService, iTerminal.Core.Services.Checksums.ChecksumService>();
        services.AddSingleton<iTerminal.Core.Services.AccessControl.IAccessControlService>(provider =>
            OperatingSystem.IsWindows()
                ? new iTerminal.Core.Services.AccessControl.WindowsAccessControlService()
                : new iTerminal.Core.Services.AccessControl.UnixAccessControlService(
                    provider.GetRequiredService<iTerminal.Core.Services.Processes.IExternalProcessRunner>()));
        services.AddTransient<AccessControlDialog>();
        services.AddTransient<PropertiesDialog>();
        services.AddTransient<ChecksumDialog>();
        // Ctrl+Shift+F8 secure wipe, Ctrl+Shift+F7 signatures, Ctrl+Shift+F6 duplicates (sign and verify).
        services.AddSingleton<iTerminal.Core.Services.Wipe.ISecureWipeService, iTerminal.Core.Services.Wipe.SecureWipeService>();
        services.AddSingleton<iTerminal.Core.Services.Signing.ISignatureService, iTerminal.Core.Services.Signing.SignatureService>();
        // Scan with Microsoft Defender (Windows): the file panel's context menu and the File menu.
        services.AddSingleton<iTerminal.Core.Services.Defender.IMalwareScanService, iTerminal.Core.Services.Defender.DefenderScanService>();
        services.AddTransient<SecureWipeDialog>();
        services.AddTransient<SignatureDialog>();
        // Ctrl+P: print the file under the cursor and manage the print queue.
        services.AddSingleton<iTerminal.Core.Services.Printing.IPrintService, iTerminal.Core.Services.Printing.PrintService>();
        services.AddTransient<PrintDialog>();
        // File associations: Enter and F4 open a file with the program chosen for it; Shift+F3 and
        // Ctrl+F4 choose. The dialogs are handed out by a factory because one consumer needs two —
        // "Open with" and "Edit with" are separate controls.
        services.AddSingleton<iTerminal.Core.Services.FileAssociations.IFileAssociationService, iTerminal.Core.Services.FileAssociations.FileAssociationService>();
        services.AddSingleton<iTerminal.Core.Services.FileAssociations.IFileLauncher, iTerminal.Core.Services.FileAssociations.FileLauncher>();
        services.AddSingleton<iTerminal.Core.Services.FileAssociations.FileAssociationRunner>();
        services.AddTransient<FileAssociationDialog>();
        services.AddTransient<OpenWithDialog>();
        services.AddSingleton<Func<OpenWithDialog>>(provider => () => provider.GetRequiredService<OpenWithDialog>());
        services.AddSingleton<FileSystemResolver>();
        services.AddTransient<CompareFoldersDialog>();
        services.AddTransient<SynchronizeFoldersDialog>();
        services.AddTransient<StripMetadataDialog>();
        services.AddTransient<FileLabelsDialog>();
        services.AddTransient<LabelFilterDialog>();
        services.AddTransient<FileDiffDialog>();
        // Ctrl+O: the terminal handed to an interactive shell. One per control it is attached to (the
        // content and the Navigate menu); the service, which knows whether a shell is running, is one.
        services.AddSingleton<iTerminal.Core.Services.Shell.IInteractiveShellService, iTerminal.Core.Services.Shell.InteractiveShellService>();
        services.AddTransient<iTerminal.Core.Commands.InteractiveShellCommand>();
        services.AddSingleton<iTerminal.Core.Services.Archives.IArchiveDriveService, iTerminal.Core.Services.Archives.ArchiveDriveService>();
        services.AddSingleton<iTerminal.Core.Services.Preview.IFilePreviewService, iTerminal.Core.Services.Preview.FilePreviewService>();
        services.AddTransient<iTerminal.Core.Dialogs.Search.FindFilesDialog>();
        services.AddSingleton<IJob, DriveInfoJob>();
        services.AddSingleton<IJob, NetworkInfoJob>();
        services.AddSingleton<IJob, SettingsSynchronizationJob>();
        services.AddSingleton<IJob, MemoryDriveSynchronizationJob>();
        services.AddSingleton<IJob, FolderIndexJob>();
        services.AddSingleton<IJob, OnlineQuoteJob>();
        services.AddSingleton<IJob, WeatherJob>();
        services.AddSingleton<DriveInfoModel>();
        services.AddTransient<CopyDialog>();
        services.AddTransient<MoveDialog>();
        services.AddTransient<AboutDialog>();
        services.AddTransient<SystemInfoDialog>();
        services.AddSingleton<iTerminal.Core.Services.SystemSettings.ISystemSettingsService, iTerminal.Core.Services.SystemSettings.SystemSettingsService>();
        services.AddTransient<SystemSettingsDialog>();
        services.AddTransient<NetworkManagerDialog>();
        services.AddTransient<NetworkBrowserDialog>();
        services.AddTransient<IPhoneBrowserDialog>();
        services.AddSingleton<NetworkInfoModel>();
        services.AddTransient<FolderSelectorDialog>();
        services.AddSingleton<ProgressWindowFactory>();
        // The system clipboard, for Ctrl+C/X/V on files; the in-process one answers where it cannot be reached.
        services.AddSingleton<iTerminal.Core.Services.Clipboard.IFileClipboardService>(provider =>
        {
            iTerminal.Core.Services.Clipboard.IFileClipboardService system = OperatingSystem.IsWindows()
                ? new iTerminal.Core.Services.Clipboard.WindowsFileClipboardService()
                : OperatingSystem.IsLinux()
                    ? new iTerminal.Core.Services.Clipboard.LinuxFileClipboardService(
                        provider.GetRequiredService<iTerminal.Core.Services.Processes.IExternalProcessRunner>())
                    : new iTerminal.Core.Services.Clipboard.InProcessFileClipboardService();
            return new iTerminal.Core.Services.Clipboard.FallbackFileClipboardService(
                system, new iTerminal.Core.Services.Clipboard.InProcessFileClipboardService());
        });
        // The text clipboard of the text boxes and the editor (Ctrl+C/X/V/A, Ctrl+Insert, Shift+Delete/Insert); a hosted
        // service hands it to the controls, which otherwise copy to an in-process one.
        services.AddSingleton<iConsole.Services.ITextClipboardService>(provider =>
        {
            iConsole.Services.ITextClipboardService system = OperatingSystem.IsWindows()
                ? new iTerminal.Core.Services.Clipboard.WindowsTextClipboardService()
                : new iTerminal.Core.Services.Clipboard.ProcessTextClipboardService(
                    provider.GetRequiredService<iTerminal.Core.Services.Processes.IExternalProcessRunner>());
            // OSC 52 asks the terminal to set the clipboard: the fallback when no system clipboard works, and the first choice
            // over SSH. The terminal's clipboard cannot be read, so paste uses the system one and then the application's own copy.
            iConsole.Services.ITextClipboardService terminal = new iConsole.Services.Osc52TextClipboardService(
                () => Console.Out, provider.GetRequiredService<iConsole.Services.ConsoleOwnership>());
            return new iTerminal.Core.Services.Clipboard.LayeredTextClipboardService(
                system,
                terminal,
                new iConsole.Services.InProcessTextClipboardService(),
                iTerminal.Core.Services.Clipboard.LayeredTextClipboardService.IsRemoteSession());
        });
        services.AddHostedService<iTerminal.Core.Services.Clipboard.TextClipboardHostedService>();
        // Stalls of the main loop (a key handler or render pass past its 200 ms budget) go to the log.
        services.AddHostedService<iTerminal.Core.Services.Diagnostics.MainLoopWatchDogLoggingHostedService>();
        // The taskbar's progress indicator: Windows drives the taskbar button, everywhere else it is a no-op. A job keeps it in
        // step with the background tasks.
        services.AddSingleton<iTerminal.Core.Services.Taskbar.ITaskbarProgress>(_ => OperatingSystem.IsWindows()
            ? new iTerminal.Core.Services.Taskbar.WindowsTaskbarProgress()
            : iTerminal.Core.Services.Taskbar.NullTaskbarProgress.Instance);
        services.AddSingleton<iConsole.Jobs.IJob, iTerminal.Core.Services.Taskbar.TaskbarProgressJob>();
        // The terminal's title follows the folder of the focused panel and is put back when the application stops.
        services.AddSingleton<iConsole.Jobs.IJob, iTerminal.Core.Services.Terminal.TerminalTitleJob>();
        services.AddSingleton<iTerminal.Core.Services.Clipboard.FileClipboardOperations>();
        services.AddSingleton<ISettingsService, SettingsService>();
        // Each configurable background service's start mode (BackgroundStart.<service>, default on demand).
        services.AddSingleton<iTerminal.Core.Services.BackgroundStart.IBackgroundStartService, iTerminal.Core.Services.BackgroundStart.BackgroundStartService>();
        // Where a local delete with the bin ticked goes on Linux: the freedesktop Trash, home or per-volume.
        services.AddSingleton<iTerminal.Core.Services.Recycle.ITrashService, iTerminal.Core.Services.Recycle.FreedesktopTrash>();
        services.AddTransient<IFileSystemService, FileSystemService>();
        services.AddSingleton<IMemoryDriveService, MemoryDriveService>();
        // SSH host keys and FTPS certificates seen so far: trusted on first use, refused when they change.
        services.AddSingleton<ITrustedHostStore>(new TrustedHostStore(Path.Combine(databaseDirectory, "known_hosts.json")));
        services.AddTransient<ISshFileSystemService, SshFileSystemService>();
        services.AddTransient<ISmbFileSystemService, SmbFileSystemService>();
        // One connection for the run: both panels may be on the same FTP server.
        services.AddSingleton<IFtpFileSystemService, FtpFileSystemService>();
        // Likewise for WebDAV (dav:// and davs://): one HttpClient, replaced when the host or user changes.
        services.AddSingleton<IWebDavFileSystemService, WebDavFileSystemService>();
        // mtp://: phones and cameras, through the platform's own stack — GVfs's gio on Linux, the
        // shell namespace on Windows. A test registers a backend of its own.
        services.AddSingleton<iTerminal.Core.Services.Mtp.IMtpBackend>(provider =>
        {
            var runner = provider.GetRequiredService<iTerminal.Core.Services.Processes.IExternalProcessRunner>();
            if (OperatingSystem.IsWindows())
            {
                return new iTerminal.Core.Services.Mtp.ShellMtpBackend(runner);
            }

            return OperatingSystem.IsLinux()
                ? new iTerminal.Core.Services.Mtp.GioMtpBackend(runner)
                : new iTerminal.Core.Services.Mtp.UnavailableMtpBackend("MTP devices cannot be browsed on this system.");
        });
        services.AddSingleton<iTerminal.Core.Services.Mtp.IMtpDriveService, iTerminal.Core.Services.Mtp.MtpDriveService>();
        // The cloud drives: s3://, onedrive:// and gdrive://. One client for the run each; the OAuth
        // ones sign in through the device flow and keep their refresh token in the password manager.
        services.AddSingleton<iTerminal.Core.Services.Cloud.IS3FileSystemService>(provider =>
            new iTerminal.Core.Services.Cloud.S3FileSystemService(provider.GetService<Serilog.ILogger>()));
        services.AddSingleton<iTerminal.Core.Services.Cloud.IOneDriveFileSystemService>(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<iTerminal.Core.Services.Cloud.CloudOptions>>().Value;
            var tokens = new iTerminal.Core.Services.Cloud.OAuthTokenProvider(
                iTerminal.Core.Services.Cloud.OAuthTokenProvider.OneDrive(options.OneDrive),
                provider.GetService<IPasswordManager>(),
                provider.GetService<iConsole.Services.INotificationService>(),
                provider.GetService<Serilog.ILogger>());
            return new iTerminal.Core.Services.Cloud.OneDriveFileSystemService(tokens, provider.GetService<Serilog.ILogger>());
        });
        services.AddSingleton<iTerminal.Core.Services.Cloud.IGoogleDriveFileSystemService>(provider =>
        {
            var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<iTerminal.Core.Services.Cloud.CloudOptions>>().Value;
            var tokens = new iTerminal.Core.Services.Cloud.OAuthTokenProvider(
                iTerminal.Core.Services.Cloud.OAuthTokenProvider.GoogleDrive(options.GoogleDrive),
                provider.GetService<IPasswordManager>(),
                provider.GetService<iConsole.Services.INotificationService>(),
                provider.GetService<Serilog.ILogger>());
            return new iTerminal.Core.Services.Cloud.GoogleDriveFileSystemService(tokens, provider.GetService<Serilog.ILogger>());
        });
        services.AddSingleton<IIPhoneFileSystemService, IPhoneFileSystemService>();
        services.AddSingleton<IIPhoneDeviceService, IPhoneDeviceService>();
        services.AddTransient<IArchiveFileSystemService, ArchiveFileSystemService>();
        services.AddSingleton<IPasswordManager>(serviceProvider => new PasswordManager(
            Path.Combine(databaseDirectory, "passwords.sqlite"),
            Path.Combine(databaseDirectory, "keys"),
            serviceProvider.GetRequiredService<IDbContextFactory<iTerminalDbContext>>()));
        services.AddSingleton<INetworkProfileService, NetworkProfileService>();
        services.AddSingleton<INetworkDiscoveryService, NetworkDiscoveryService>();
        services.AddSingleton<OnlineQuoteService>();
        // Components downloaded into the data folder instead of shipped (see RuntimeDependencyService). The
        // Playwright driver and its Chromium are what the quote indicator needs; the list is one entry per component.
        string runtimesDirectory = Path.Combine(databaseDirectory, "runtimes");
        services.AddSingleton<IRuntimeDependency>(_ => new PlaywrightRuntimeDependency(runtimesDirectory));

        // git is not downloaded into the data folder: when it is missing the system's package manager installs it.
        services.AddSingleton<IRuntimeDependency, GitRuntimeDependency>();

        // Likewise ffmpeg and ffprobe, for the video and picture viewers and the audio formats without a managed decoder.
        services.AddSingleton<IRuntimeDependency, FfmpegRuntimeDependency>();

        // Ollama is a dependency only when the AI prompt is configured to use it. Registration cannot be skipped
        // from inside a factory, so the configured value is read here, once.
        AiOptions ai = new();
        configuration?.GetSection("AI").Bind(ai);
        if (ai.Provider == AiProvider.Ollama)
        {
            services.AddSingleton(_ => new OllamaServer(new Uri(ai.ApiBaseUrl)));
            services.AddSingleton<IRuntimeDependency>(sp => new OllamaRuntimeDependency(
                Path.Combine(runtimesDirectory, "ollama"),
                ai.ModelId,
                sp.GetRequiredService<OllamaServer>()));
            services.AddSingleton<IJob, OllamaServerJob>();
        }

        services.AddSingleton<IRuntimeDependencyService, RuntimeDependencyService>();
        services.AddSingleton<IJob, RuntimeDependencyCheckJob>();
        services.AddTransient<RuntimeDependencyDialog>();
        services.AddSingleton<iTerminal.Core.Services.Updates.IUpdateService, iTerminal.Core.Services.Updates.UpdateService>();
        services.AddSingleton<IJob, UpdateCheckJob>();
        services.AddTransient<UpdateDialog>();
        services.AddSingleton<WeatherService>();
        services.AddSingleton<FolderIndexModel>();
        services.AddTransient<TerminalWindow>();
        services.AddTransient<TerminalPanel>();
        services.AddSingleton<TerminalModel>();
        services.AddTransient<TerminalDialog>();
        services.AddSingleton<Serilog.ILogger>(provider =>
            new Logger.Logger(provider.GetRequiredService<ISettingsService>(), databaseDirectory));
        services.AddSingleton<IMemoryCacheService, MemoryCacheService>();
        return services;
    }
}
