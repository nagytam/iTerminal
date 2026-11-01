using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Model.ContentModels;
using System.Collections.Concurrent;

namespace iTerminal.Core.Services;

public class ContentModelService(
    TaskManager taskManager,
    ISettingsService settingsService,
    IFileSystemService fileSystemService,
    IMemoryDriveService memoryDriveService,
    ISshFileSystemService sshFileSystemService,
    ISmbFileSystemService smbFileSystemService,
    IIPhoneFileSystemService iPhoneFileSystemService,
    INetworkProfileService networkProfileService,
    IPasswordManager passwordManager,
    IArchiveFileSystemService archiveFileSystemService,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IMemoryCacheService memoryCacheService,
    Serilog.ILogger logger,
    Microsoft.EntityFrameworkCore.IDbContextFactory<iTerminal.Core.Data.iTerminalDbContext>? dbContextFactory = null,
    IStopwatchFactory? stopwatchFactory = null,
    iTerminal.Core.Services.Music.IMusicPlayerService? musicPlayerService = null,
    iTerminal.Core.Services.Music.IMusicLibraryService? musicLibraryService = null,
    iTerminal.Core.Services.Music.IAudioDeviceService? audioDeviceService = null,
    iTerminal.Core.Services.Music.IMusicSessionRestoreService? musicSessionRestoreService = null,
    iTerminal.Core.Services.Git.IGitDriveService? gitDriveService = null,
    iTerminal.Core.Services.Docker.IDockerDriveService? dockerDriveService = null,
    iTerminal.Core.Services.Search.IFileSearchService? fileSearchService = null,
    iTerminal.Core.Services.Preview.IFilePreviewService? filePreviewService = null,
    iTerminal.Core.Services.Archives.IArchiveDriveService? archiveDriveService = null,
    iTerminal.Core.Services.Recycle.IRecycleBinService? recycleBinService = null,
    IFtpFileSystemService? ftpFileSystemService = null,
    IWebDavFileSystemService? webDavFileSystemService = null,
    iTerminal.Core.Services.Mtp.IMtpDriveService? mtpDriveService = null,
    iTerminal.Core.Services.Cloud.IS3FileSystemService? s3FileSystemService = null,
    iTerminal.Core.Services.Cloud.IOneDriveFileSystemService? oneDriveFileSystemService = null,
    iTerminal.Core.Services.Cloud.IGoogleDriveFileSystemService? googleDriveFileSystemService = null,
    Microsoft.Extensions.Options.IOptions<iTerminal.Core.Services.Cloud.CloudOptions>? cloudOptions = null)
    : IContentModelService, IDisposable
{
    /// <summary>The singletons the music drives are views over, resolved together.</summary>
    private sealed record MusicServices(
        iTerminal.Core.Services.Music.IAudioDeviceService Devices,
        iTerminal.Core.Services.Music.IMusicPlayerService Player,
        iTerminal.Core.Services.Music.IMusicLibraryService Library,
        iTerminal.Core.Services.Music.IMusicSessionRestoreService SessionRestore);

    /// <summary>
    /// Resolved lazily and only substituted when the container did not supply them: building a
    /// player eagerly would start its pump thread in every test that happens to construct a
    /// <see cref="ContentModelService"/>, and almost none of them ever open a music panel.
    /// </summary>
    private readonly Lazy<MusicServices> _musicServices = new(() =>
    {
        var devices = audioDeviceService ?? new iTerminal.Core.Services.Music.AudioDeviceService([]);
        var player = musicPlayerService ?? new iTerminal.Core.Services.Music.MusicPlayerService(devices, logger);
        return new MusicServices(
            devices,
            player,
            musicLibraryService ?? new iTerminal.Core.Services.Music.MusicLibraryService(dbContextFactory, settingsService, logger),
            // The substitute reads the queue table and nothing else: importing the pre-database
            // queue file is the app's start-up path, and a fallback built here — in a test, or in a
            // run with no database — has no business reaching into the user's playlists folder.
            musicSessionRestoreService ?? new iTerminal.Core.Services.Music.MusicSessionRestoreService(
                player,
                new iTerminal.Core.Services.Music.MusicPlaylistStore(dbContextFactory, logger),
                playlistService: null,
                settingsService));
    });

    /// <summary>Substituted only when the container did not supply them, as the music services are.</summary>
    private readonly Lazy<iTerminal.Core.Services.Git.IGitDriveService> _gitDriveService = new(() =>
        gitDriveService ?? new iTerminal.Core.Services.Git.GitDriveService(new iTerminal.Core.Services.Processes.ExternalProcessRunner(), settingsService));

    private readonly Lazy<iTerminal.Core.Services.Docker.IDockerDriveService> _dockerDriveService = new(() =>
        dockerDriveService ?? new iTerminal.Core.Services.Docker.DockerDriveService(new iTerminal.Core.Services.Processes.ExternalProcessRunner()));

    private readonly Lazy<iTerminal.Core.Services.Recycle.IRecycleBinService> _recycleBinService = new(() =>
        recycleBinService ?? new iTerminal.Core.Services.Recycle.RecycleBinService(iTerminal.Core.Services.Recycle.RecycleBinService.CreateStore()));

    private readonly Lazy<iTerminal.Core.Services.Archives.IArchiveDriveService> _archiveDriveService = new(() =>
        archiveDriveService ?? new iTerminal.Core.Services.Archives.ArchiveDriveService(new iTerminal.Core.Services.Processes.ExternalProcessRunner()));

    private readonly Lazy<iTerminal.Core.Services.Search.IFileSearchService> _fileSearchService = new(() =>
        fileSearchService ?? new iTerminal.Core.Services.Search.FileSearchService(logger));

    private readonly Lazy<iTerminal.Core.Services.Preview.IFilePreviewService> _filePreviewService = new(() =>
        filePreviewService ?? new iTerminal.Core.Services.Preview.FilePreviewService());

    public ConcurrentList<IContentModel> ContentModels { get; private set; } = [];

    /// <summary>
    /// Disposes the models still on screen when the application stops. A file panel's folder watcher is an
    /// operating-system handle — on Linux an inotify instance, of which a user has 128 — and nothing else
    /// releases it: the service is a singleton, so the models outlived the application's host and the
    /// instances piled up across every run in one process until new watchers could not be created.
    /// </summary>
    public void Dispose()
    {
        foreach (IContentModel contentModel in ContentModels.ToList())
        {
            (contentModel as IDisposable)?.Dispose();
        }
    }
    public ConcurrentList<IContentBrowser> ContentBrowsers { get; private set; } = [];
    public ConcurrentDictionary<string, long> CursorPositions { get; private set; } = [];
    public string FocusedPanel { get; set; } = string.Empty;
    public string LastFocusedPanel { get; set; } = string.Empty;

    public event EventHandler? ModelChanged;

    protected virtual void OnModelChanged(EventArgs e)
    {
        ModelChanged?.Invoke(this, e);
    }

    public IContentModel? AddContentModel(string name, Url url)
    {
        IContentModel? contentModel = null;

        switch (url.ContainerType)
        {
            case ContainerType.FileSystem:
                {
                contentModel = new ContentModelFileSystem(
                    name,
                    taskManager,
                    settingsService,
                    folderHistoryService,
                    fileSystemService,
                    frequentExecutionGuardService,
                    memoryCacheService,
                    logger,
                    dbContextFactory,
                    stopwatchFactory);
                    break;
                }
            case ContainerType.FileSystemArchive:
                {
                    contentModel = new ContentModelFileSystemArchive(name, taskManager, settingsService, folderHistoryService, frequentExecutionGuardService, archiveFileSystemService);
                    break;
                }
            case ContainerType.MemoryDrive:
                {
                    contentModel = new ContentModelMemoryDrive(name, settingsService, memoryDriveService, folderHistoryService, frequentExecutionGuardService);
                    break;
                }
            case ContainerType.SshFileSystem:
                {
                    contentModel = new ContentModelSshDrive(name, settingsService, sshFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger, networkProfileService, passwordManager);
                    break;
                }
            case ContainerType.SmbFileSystem:
                {
                    contentModel = new ContentModelSmbDrive(name, settingsService, smbFileSystemService, networkProfileService, passwordManager, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger);
                    break;
                }
            case ContainerType.FtpFileSystem when ftpFileSystemService is not null:
                {
                    contentModel = new ContentModelFtpDrive(name, settingsService, ftpFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger, networkProfileService, passwordManager);
                    break;
                }
            case ContainerType.WebDav when webDavFileSystemService is not null:
                {
                    contentModel = new ContentModelWebDavDrive(name, settingsService, webDavFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger, networkProfileService, passwordManager);
                    break;
                }
            case ContainerType.S3 when s3FileSystemService is not null:
                {
                    contentModel = new ContentModelS3Drive(name, settingsService, s3FileSystemService, folderHistoryService, frequentExecutionGuardService, logger, passwordManager, cloudOptions?.Value.S3);
                    break;
                }
            case ContainerType.OneDrive when oneDriveFileSystemService is not null:
                {
                    contentModel = new ContentModelOneDriveDrive(name, settingsService, oneDriveFileSystemService, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.GoogleDrive when googleDriveFileSystemService is not null:
                {
                    contentModel = new ContentModelGoogleDriveDrive(name, settingsService, googleDriveFileSystemService, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.IPhoneFileSystem:
                {
                    contentModel = new ContentModelIPhoneDrive(name, settingsService, iPhoneFileSystemService, folderHistoryService, frequentExecutionGuardService, memoryCacheService, logger);
                    break;
                }
            case ContainerType.MusicPlaylist:
                {
                    contentModel = new ContentModelMusicPlaylist(
                        name,
                        settingsService,
                        _musicServices.Value.Player,
                        _musicServices.Value.Devices,
                        folderHistoryService,
                        frequentExecutionGuardService,
                        _musicServices.Value.SessionRestore);
                    break;
                }
            case ContainerType.MusicCatalog:
                {
                    // The catalog is what asks for the music library to be scanned (see BackgroundStart.MusicLibrary).
                    _musicServices.Value.Library.RequestIndexing();
                    contentModel = new ContentModelMusicCatalog(
                        name,
                        settingsService,
                        _musicServices.Value.Library,
                        _musicServices.Value.Player,
                        folderHistoryService,
                        frequentExecutionGuardService);
                    break;
                }
            case ContainerType.Git:
                {
                    contentModel = new ContentModelGitDrive(name, settingsService, _gitDriveService.Value, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.Docker:
                {
                    contentModel = new ContentModelDockerDrive(name, settingsService, _dockerDriveService.Value, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.Archive:
                {
                    contentModel = new ContentModelArchiveDrive(name, settingsService, _archiveDriveService.Value, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.Mtp when mtpDriveService is not null:
                {
                    contentModel = new ContentModelMtpDrive(name, settingsService, mtpDriveService, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.Recycle:
                {
                    contentModel = new ContentModelRecycleDrive(name, settingsService, _recycleBinService.Value, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.SearchResults:
                {
                    contentModel = new ContentModelSearchResults(name, settingsService, _fileSearchService.Value, fileSystemService, folderHistoryService, frequentExecutionGuardService, logger);
                    break;
                }
            case ContainerType.Preview:
                {
                    contentModel = new ContentModelPreview(name, settingsService, this, _filePreviewService.Value, frequentExecutionGuardService);
                    break;
                }
        }
        if (contentModel is not null)
        {
            var oldContentModel = ContentModels.FirstOrDefault(m => m.Name == contentModel.Name);
            if (oldContentModel is not null)
            {
                ContentModels.Remove(oldContentModel);
                oldContentModel.Entries.CollectionChanged -= OnEntriesCollectionChanged;
                oldContentModel.CursorPosition.PropertyChanged -= OnCursorPositionPropertyChanged;
                // A replaced model is dead, but a music model has subscribed to the player's and the
                // library's events, which would otherwise keep it alive and repainting for the rest
                // of the session. Everything else here ignores the call.
                (oldContentModel as IDisposable)?.Dispose();
            }
            ContentModels.Add(contentModel);
        }
        var currentContentModel = GetFocusedModel();
        if (currentContentModel?.Url?.FullUrl is not null)
        {
            CursorPositions[currentContentModel.Url.FullUrl] = currentContentModel.CursorPosition.Y;
        }
        var contentBrowser = ContentBrowsers.FirstOrDefault(b => b.Name == name);
        contentModel!.ContentBrowser = contentBrowser!;

        // The sort is the panel's, not the drive's: a navigation that crosses drive kinds makes a
        // new model, and it starts in the order the panel was in.
        if (contentModel is iTerminal.Core.Model.ISortableContentModel sortable)
        {
            sortable.SetSort(iTerminal.Core.Model.PanelSort.Parse(settingsService?.Get(iTerminal.Core.Model.PanelSort.SettingsKey(name))));
            sortable.SetViewOptions(iTerminal.Core.Model.PanelViewOptions.Load(settingsService));
        }

        contentModel.SetUrl(url);
        contentModel.Entries.CollectionChanged += OnEntriesCollectionChanged;
        contentModel.CursorPosition.PropertyChanged += OnCursorPositionPropertyChanged;
        OnModelChanged(new EventArgs());
        if (CursorPositions.TryGetValue(contentModel.Url.FullUrl, out var cursorPositionY))
        {
            contentModel.CursorPosition.Y = cursorPositionY.Range(0, Math.Max(0, contentModel.ScrollableSize.Height - 1));
        }
        return contentModel;
    }

    private void OnEntriesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        logger.Debug($"{nameof(ContentModelService)} {nameof(OnEntriesCollectionChanged)}");
        OnModelChanged(new EventArgs());
    }

    private void OnCursorPositionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        logger.Debug($"{nameof(ContentModelService)} {nameof(OnCursorPositionPropertyChanged)}");
        OnModelChanged(new EventArgs());
    }

    public IContentModel? GetModel(string name)
    {
        var contentModel = ContentModels.FirstOrDefault(m => m.Name == name);
        return contentModel;
    }

    public IContentModel? GetLastContentModel()
    {
        var contentModel = ContentModels.FirstOrDefault(m => m.Name == LastFocusedPanel);
        return contentModel;
    }

    public IContentModel? GetFocusedModel()
    {
        var contentModel = ContentModels.FirstOrDefault(m => m.Name == FocusedPanel);
        return contentModel;
    }

    public void AddContentBrowser(string name, IContentBrowser contentBrowser)
    {
        ContentBrowsers.Add(contentBrowser);
    }

    public async Task SetUrlAsync(Url url)
    {
        var contentModel = GetLastContentModel();
        if (contentModel is not null)
        {
            var left = contentModel.Url;
            if (contentModel.Url?.FullUrl is not null)
            {
                CursorPositions[contentModel.Url.FullUrl] = contentModel.CursorPosition.Y;
            }
            if (url.ContainerType != left!.ContainerType)
            {
                ContentModelAsyncDrive.RememberReturnUrl(settingsService, contentModel.Name, contentModel.Url, url);
                contentModel = AddContentModel(contentModel.Name, url);
                if (contentModel is not null)
                {
                    await contentModel.ContentBrowser.ChangeModelAsync(contentModel.Name);
                    LandOnTheArchiveLeft(contentModel, left);
                }
            }
            else
            {
                contentModel?.SetUrl(url);
                ReturnToRememberedRow(contentModel, left);
            }
        }
    }

    public async Task SetUrlAsync(string name, Url url)
    {
        var contentModel = GetModel(name);
        if (contentModel is not null)
        {
            var left = contentModel.Url;
            if (contentModel.Url?.FullUrl is not null)
            {
                CursorPositions[contentModel.Url.FullUrl] = contentModel.CursorPosition.Y;
            }
            if (url.ContainerType != left!.ContainerType)
            {
                ContentModelAsyncDrive.RememberReturnUrl(settingsService, name, contentModel.Url, url);
                contentModel = AddContentModel(name, url);
                if (contentModel is not null)
                {
                    await contentModel.ContentBrowser.ChangeModelAsync(name);
                    LandOnTheArchiveLeft(contentModel, left);
                }
            }
            else
            {
                contentModel?.SetUrl(url);
                ReturnToRememberedRow(contentModel, left);
            }
        }
    }

    /// <summary>
    /// Going up puts the cursor on the folder that was just left — the row the user came out of,
    /// which is what makes going in and out of folders with the keyboard cheap. Enter on ".." did
    /// this only by luck, Ctrl+PgUp not at all: the position was remembered as a row index keyed by
    /// the URL string, which a folder still being listed cannot honour (it has one row), and which
    /// Ctrl+PgUp's <c>Url.ParentUrl</c> — written with a trailing separator — never matched. The
    /// restore is handed to the model instead, which holds it until the listing that has the row
    /// arrives, and finds the row by URL.
    /// </summary>
    /// <summary>
    /// After a navigation within one drive: back to the row this folder was left on (by index, from
    /// <see cref="CursorPositions"/>) — or, going up, to the folder just left (by URL). Both are handed
    /// to the model as a restore rather than written into the cursor: the folder has not been listed
    /// yet, so an index written now is clamped against the ".." row — and it used to be written as
    /// <c>ScrollableSize.Height - 1</c>, which is -1 while the listing is momentarily empty. That
    /// tripped <see cref="Position"/>'s assertion, which ended the key handler before the restore
    /// below was ever asked for: Ctrl+PgUp then left the cursor on "..".
    /// </summary>
    private void ReturnToRememberedRow(IContentModel? contentModel, Url? left)
    {
        if (contentModel?.Url?.FullUrl is null)
        {
            return;
        }

        CursorPositions.TryGetValue(contentModel.Url.FullUrl, out var rememberedY);
        if (!LandOnTheFolderLeft(contentModel, left, rememberedY) && rememberedY > 0)
        {
            contentModel.RestoreNavigation(null, rememberedY, 0);
        }
    }

    /// <summary>
    /// Leaving an archive (its top level's <c>..</c>) for the folder it is in puts the cursor on the
    /// archive — the same courtesy as going up out of a folder.
    /// </summary>
    internal static void LandOnTheArchiveLeft(IContentModel contentModel, Url? left)
    {
        if (left?.ContainerType != ContainerType.Archive || contentModel.Url?.ContainerType != ContainerType.FileSystem)
        {
            return;
        }

        var (archivePath, _) = iTerminal.Core.Services.Archives.ArchiveDriveUrl.Parse(left);
        var folder = Path.GetDirectoryName(archivePath);
        if (!string.IsNullOrEmpty(folder)
            && string.Equals(folder.TrimEnd(Path.DirectorySeparatorChar), contentModel.Url.FullName.TrimEnd(Path.DirectorySeparatorChar, '/'), StringComparison.OrdinalIgnoreCase))
        {
            contentModel.RestoreNavigation(new Url(archivePath).FullUrl, 0, 0);
        }
    }

    internal static bool LandOnTheFolderLeft(IContentModel? contentModel, Url? left, long fallbackCursorY)
    {
        var arrived = contentModel?.Url?.FullUrl?.TrimEnd('/', '\\');
        var leftUrl = left?.FullUrl?.TrimEnd('/', '\\');
        if (contentModel is null || string.IsNullOrEmpty(arrived) || string.IsNullOrEmpty(leftUrl)
            || leftUrl.Length <= arrived.Length + 1
            || !leftUrl.StartsWith(arrived, StringComparison.Ordinal)
            || leftUrl[arrived.Length] is not ('/' or '\\'))
        {
            return false;
        }

        // The child of the folder arrived in that lies on the way to the one left.
        var next = leftUrl.IndexOfAny(['/', '\\'], arrived.Length + 1);
        var child = next < 0 ? leftUrl : leftUrl[..next];
        contentModel.RestoreNavigation(child, fallbackCursorY, 0);
        return true;
    }

    public const string Left = "LeftPanel";
    public const string Right = "RightPanel";

    public Url Url
    {
        get
        {
            var leftModel = ContentModels.FirstOrDefault(m => m.Name == Left);
            var rightModel = ContentModels.FirstOrDefault(m => m.Name == Right);
            if (leftModel is not null && leftModel.ContentBrowser.IsFocused)
            {
                return leftModel.Url;
            }
            else if (rightModel is not null)
            {
                return rightModel.Url;
            }
            return new Url(Directory.GetCurrentDirectory());
        }
    }

    public Url? FocusedFileUrl
    {
        get
        {
            Url? filePath;
            var leftModel = ContentModels.FirstOrDefault(m => m.Name == Left);
            var rightModel = ContentModels.FirstOrDefault(m => m.Name == Right);
            try
            {
                if (leftModel!.ContentBrowser.IsFocused && leftModel.CursorPosition.Y < leftModel.FilteredEntries.Count)
                {
                    filePath = leftModel.GetFocusedEntry()?.Url;
                }
                else if (rightModel!.ContentBrowser.IsFocused && rightModel.CursorPosition.Y < rightModel.FilteredEntries.Count)
                {
                    filePath = rightModel.GetFocusedEntry()?.Url;
                }
                else
                {
                    filePath = null;
                }
            }
            catch
            {
                filePath = null;
            }
            return filePath;
        }
    }
    public Url? NonFocusedFolderUrl
    {
        get
        {
            Url? filePath;
            var leftModel = ContentModels.FirstOrDefault(m => m.Name == Left);
            var rightModel = ContentModels.FirstOrDefault(m => m.Name == Right);
            try
            {
                if (leftModel!.ContentBrowser.IsFocused)
                {
                    filePath = rightModel!.Url;
                }
                else if (rightModel!.ContentBrowser.IsFocused)
                {
                    filePath = leftModel.Url;
                }
                else
                {
                    filePath = null;
                }
            }
            catch
            {
                filePath = null;
            }
            return filePath;
        }
    }
}
