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
        }

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

        services.AddDbContextFactory<iTerminalDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<ICacheService, CacheService>();
        services.AddScoped<iTerminal.Core.Services.Portfolio.IPortfolioService, iTerminal.Core.Services.Portfolio.PortfolioService>();
        services.AddSingleton<iTerminal.Core.Services.Portfolio.IQuoteProvider, iTerminal.Core.Services.Portfolio.StooqQuoteProvider>();
        services.AddHostedService<DatabaseInitializationHostedService>();
        services.AddTransient<TerminalMenuBar>();
        services.AddSingleton<Content>();
        services.AddSingleton<FilePanelFactory>();
        services.AddTransient<HotKeys>();
        services.AddTransient<ContentBrowser>();
        services.AddSingleton<iTerminal.Core.Services.Git.IGitService, iTerminal.Core.Services.Git.GitService>();
        services.AddSingleton<IContentLens, GitLens>();
        services.AddTransient<iTerminal.Core.Dialogs.Git.GitClientDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.PasswordManager.PasswordManagerStartupDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.Calculator.CalculatorDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.UnitConverter.UnitConverterDialog>();
        services.AddSingleton<iTerminal.Core.Model.Calendar.MeetingStore>();
        services.AddTransient<iTerminal.Core.Dialogs.Calendar.CalendarDialog>();
        services.AddSingleton<iTerminal.Core.Model.ToDo.ToDoStore>();
        services.AddTransient<iTerminal.Core.Dialogs.ToDo.ToDoListDialog>();
        services.AddSingleton<iTerminal.Core.Model.Mail.MailAccountStore>();
        services.AddSingleton<iTerminal.Core.Services.Mail.IMailService, iTerminal.Core.Services.Mail.MailService>();
        services.AddTransient<iTerminal.Core.Dialogs.Mail.MailDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.PasswordManager.PasswordManagerDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.Portfolio.PortfolioManagerDialog>();
        services.AddTransient<iTerminal.Core.Dialogs.Portfolio.PortfolioSetupWizardDialog>();
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
                serviceProvider.GetService<Serilog.ILogger>()));
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
        // Ctrl+F12 properties, Shift+F12 checksums.
        services.AddSingleton<iTerminal.Core.Services.Properties.IFilePropertiesService, iTerminal.Core.Services.Properties.FilePropertiesService>();
        services.AddSingleton<iTerminal.Core.Services.Checksums.IChecksumService, iTerminal.Core.Services.Checksums.ChecksumService>();
        services.AddTransient<PropertiesDialog>();
        services.AddTransient<ChecksumDialog>();
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
        services.AddSingleton<IJob, PortfolioQuoteJob>();
        services.AddSingleton<IJob, WeatherJob>();
        services.AddSingleton<DriveInfoModel>();
        services.AddTransient<CopyDialog>();
        services.AddTransient<MoveDialog>();
        services.AddTransient<AboutDialog>();
        services.AddTransient<SystemInfoDialog>();
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
        services.AddSingleton<iTerminal.Core.Services.Clipboard.FileClipboardOperations>();
        services.AddSingleton<ISettingsService, SettingsService>();
        // Where a local delete with the bin ticked goes on Linux: the freedesktop Trash, home or per-volume.
        services.AddSingleton<iTerminal.Core.Services.Recycle.ITrashService, iTerminal.Core.Services.Recycle.FreedesktopTrash>();
        services.AddTransient<IFileSystemService, FileSystemService>();
        services.AddSingleton<IMemoryDriveService, MemoryDriveService>();
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
        services.AddSingleton<WeatherService>();
        services.AddSingleton<FolderIndexModel>();
        services.AddTransient<TerminalWindow>();
        services.AddTransient<TerminalPanel>();
        services.AddSingleton<TerminalModel>();
        services.AddTransient<TerminalDialog>();
        services.AddSingleton<Serilog.ILogger, Logger.Logger>();
        services.AddSingleton<IMemoryCacheService, MemoryCacheService>();
        return services;
    }
}
