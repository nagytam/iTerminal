using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using System.Diagnostics;

namespace iTerminal.Core.Model.ContentModels;

public class ContentModelFileSystem(
    string name,
    TaskManager taskManager,
    ISettingsService settingsService,
    FolderHistoryService folderHistoryService,
    IFileSystemService fileSystemService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IMemoryCacheService memoryCacheService,
    Serilog.ILogger logger,
    Microsoft.EntityFrameworkCore.IDbContextFactory<iTerminal.Core.Data.iTerminalDbContext>? dbContextFactory = null,
    IStopwatchFactory? stopwatchFactory = null
    )
    : ContentModelBase(name, frequentExecutionGuardService), IContentModel, IDisposable, Services.Shell.IShellLaunchSource
{
    /// <summary>Ctrl+O: the user's shell, in the folder this panel shows.</summary>
    public Services.Shell.ShellLaunch? GetShellLaunch(string? configuredShell) =>
        Url is null ? null : Services.Shell.ShellLaunch.Local(Url.Path, configuredShell);

    public override bool IsReadOnly => false;

    public override void ReadUrl()
    {
        var key = Name;
        var url = _settingsService.Get(key) ?? Directory.GetCurrentDirectory();
        SetUrl(url);
    }

    public override string GetHeader(int width)
    {
        var path = Url?.Path ?? string.Empty;
        if (string.IsNullOrEmpty(path) || width <= 0)
        {
            return string.Empty;
        }
        if (path.Length <= width)
        {
            return path;
        }
        // Keep the drive prefix (e.g. "C:\") on the left and the deepest visible
        // folder components on the right with "..." in the middle.
        var dirSep = System.IO.Path.DirectorySeparatorChar;
        var firstSep = path.IndexOf(dirSep);
        var prefixLength = firstSep >= 0 ? firstSep + 1 : 0;   // e.g. "C:\" = 3 chars
        var prefix = path[..prefixLength];
        const int ellipsisLength = 3;
        var maxTailLength = width - prefixLength - ellipsisLength;
        if (maxTailLength <= 0)
        {
            // Not enough room for drive + "..." — fall back to a pure left-trim.
            var available = Math.Max(0, width - ellipsisLength);
            return available == 0 ? "..." : "..." + path[^available..];
        }
        // Ideal cut point (may bisect a folder name).
        var idealTailStart = path.Length - maxTailLength;
        // Try to snap forward to the next separator so the tail begins on a clean
        // folder boundary — but only if the resulting tail still fits within budget.
        var snapForward = path.IndexOf(dirSep, idealTailStart);
        if (snapForward > idealTailStart
            && snapForward < path.Length
            && (path.Length - snapForward) <= maxTailLength)
        {
            return prefix + "..." + path[snapForward..];
        }
        // If no forward snap fits cleanly, use the ideal character-level cut
        // (the last folder component is simply too long; show what fits).
        return prefix + "..." + path[idealTailStart..];
    }

    public override string GetFooter() => EntryCountFooter();

    protected FileSystemWatcher? FolderWatcher { get; private set; }

    private TaskManager _taskManager { get; set; } = taskManager;
    private FolderHistoryService _folderHistoryService { get; set; } = folderHistoryService;
    private ISettingsService _settingsService { get; set; } = settingsService;
    private readonly object _refreshDebounceLock = new();
    private CancellationTokenSource? _refreshDebounceCancellationTokenSource;
    // Guards against a race where a previous SchedulePrefetch Task.Run adds tasks after
    // the new navigation's cancellation sweep has already run.
    private CancellationTokenSource? _prefetchScheduleCts;
    private static readonly TimeSpan FileWatcherRefreshDelay = TimeSpan.FromMilliseconds(150);
    private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<iTerminal.Core.Data.iTerminalDbContext>? _dbContextFactory = dbContextFactory;
    private readonly IStopwatchFactory? _stopwatchFactory = stopwatchFactory;

    /// <summary>
    /// The most recently enqueued visible-panel <see cref="FilePanelRefreshTask"/>. When a
    /// <see cref="IStopwatchFactory"/> was injected, its <see cref="FilePanelRefreshTask.PhaseDurations"/>
    /// reports the actual internal cost of the last refresh (cache lookup / enumeration / UI
    /// update), independent of any scheduling delay incurred while the task waited in the
    /// <see cref="TaskManager"/> queue. Tests use this to assert performance budgets without
    /// being sensitive to contention from other tests running in parallel.
    /// </summary>
    public FilePanelRefreshTask? LastRefreshTask { get; private set; }

    public override void SetUrl(Url url)
    {
        if (!fileSystemService.ExistsFolder(url))
        {
            url = Directory.GetCurrentDirectory();
        }
        _settingsService.Set(Name, url.FullUrl);
        _folderHistoryService.Add(url);
        // Cancel any in-flight prefetch scheduling for the previous folder before
        // the task sweep below, so stale tasks cannot be added afterwards.
        var oldPrefetchCts = Interlocked.Exchange(ref _prefetchScheduleCts, new CancellationTokenSource());
        oldPrefetchCts?.Cancel();
        oldPrefetchCts?.Dispose();
        foreach (var task in _taskManager.GetTasks<Tasks.FilePanelRefreshTask>(t => t.ContentModelName == Name))
        {
            task?.CancellationTokenSource.Cancel();
        }
        Entries.Clear();
        Filter = null;
        Url = url;
        ResetNavigation();
        if (url.Path != Path.GetPathRoot(url.Path))
        {
            Entries.Add(new FileSystemEntry("..", url.ParentPath));
        }

        #region

        if (FolderWatcher is not null)
        {
            FolderWatcher.Path = url.Path;
        }
        else
        {
            FolderWatcher = new FileSystemWatcher(url.Path)
            {
                NotifyFilter = NotifyFilters.Attributes
                               | NotifyFilters.CreationTime
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.FileName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.Size
            };

            FolderWatcher.Changed += OnChanged;
            FolderWatcher.Created += OnCreated;
            FolderWatcher.Deleted += OnDeleted;
            FolderWatcher.Renamed += OnRenamed;
            FolderWatcher.Error += OnError;
        }
        #endregion

        Refresh();

        FolderWatcher.EnableRaisingEvents = true;
    }

    public void Dispose()
    {
        var prefetchCts = Interlocked.Exchange(ref _prefetchScheduleCts, null);
        prefetchCts?.Cancel();
        prefetchCts?.Dispose();

        lock (_refreshDebounceLock)
        {
            _refreshDebounceCancellationTokenSource?.Cancel();
            _refreshDebounceCancellationTokenSource?.Dispose();
            _refreshDebounceCancellationTokenSource = null;
        }

        foreach (var task in _taskManager.GetTasks<FilePanelRefreshTask>(t => t.ContentModelName == Name))
        {
            task?.CancellationTokenSource.Cancel();
        }

        if (FolderWatcher is not null)
        {
            FolderWatcher.EnableRaisingEvents = false;
            FolderWatcher.Changed -= OnChanged;
            FolderWatcher.Created -= OnCreated;
            FolderWatcher.Deleted -= OnDeleted;
            FolderWatcher.Renamed -= OnRenamed;
            FolderWatcher.Error -= OnError;
            FolderWatcher.Dispose();
            FolderWatcher = null;
        }
    }

    public override void Refresh(bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            foreach (var task in _taskManager.GetTasks<FilePanelRefreshTask>(t => t.Url.FullUrl == Url.FullUrl))
            {
                task?.CancellationTokenSource.Cancel();
            }
        }
        var hasTask = _taskManager.GetTasks<FilePanelRefreshTask>(t => t.Url.FullUrl == Url.FullUrl && !t.CancellationTokenSource.IsCancellationRequested).Any();
        if (!hasTask)
        {
            logger.Information($"Refresh task is started for {Url.FullUrl}");
            // Add refresh task for the current folder (updates UI). This is the high-priority,
            // visible work and is enqueued synchronously so navigation reflects immediately.
            var refreshTask = new FilePanelRefreshTask(this, memoryCacheService, logger, _dbContextFactory, _stopwatchFactory);
            LastRefreshTask = refreshTask;
            _taskManager.Add(refreshTask);

            // Schedule the background-only prefetch of neighbouring folders off the calling
            // (input) thread: enumerating child directories can be slow for large folders and
            // must never block key handling. These are lower-priority background cache tasks.
            // Capture the token at scheduling time; if the user navigates away before
            // SchedulePrefetch starts, the token is already cancelled and it returns immediately.
            var prefetchUrl = Url;
            var prefetchToken = _prefetchScheduleCts?.Token ?? CancellationToken.None;
            Task.Run(() => SchedulePrefetch(prefetchUrl, prefetchToken));
        }
        else
        {
            logger.Information($"Refresh task is running already for {Url.FullUrl}");
        }
    }

    /// <summary>
    /// Enqueues background-only cache tasks for the parent folder and all immediate child folders
    /// so that switching to them later can be instant (served from the memory/DB cache).
    /// Runs off the input thread; best-effort only. Respects <paramref name="cancellationToken"/>
    /// so that navigating away before this method starts (or midway through) stops all work
    /// immediately without leaving stale tasks in the queue.
    /// </summary>
    private void SchedulePrefetch(Url folderUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested) return;

            // Parent folder
            if (folderUrl.Path != Path.GetPathRoot(folderUrl.Path))
            {
                var parent = folderUrl.ParentUrl;
                if (!cancellationToken.IsCancellationRequested && !HasPendingTaskFor(parent))
                {
                    _taskManager.Add(new FilePanelRefreshTask(parent, Name, memoryCacheService, logger, _dbContextFactory));
                }
            }

            // Child folders
            var dir = new DirectoryInfo(folderUrl.Path);
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (sub is null) continue;
                try
                {
                    var childUrl = new Url(sub);
                    if (!HasPendingTaskFor(childUrl))
                    {
                        _taskManager.Add(new FilePanelRefreshTask(childUrl, Name, memoryCacheService, logger, _dbContextFactory));
                    }
                }
                catch
                {
                    // Ignore individual child failures; best-effort caching only
                }
            }
        }
        catch
        {
            // Best-effort only; swallow any errors to avoid affecting UI refresh
        }
    }

    private bool HasPendingTaskFor(Url url) =>
        _taskManager.GetTasks<FilePanelRefreshTask>(
            t => t.Url.FullUrl == url.FullUrl && !t.CancellationTokenSource.IsCancellationRequested).Any();

    private void OnError(object sender, ErrorEventArgs e)
    {
        if (sender is FileSystemWatcher)
        {
            RequestRefreshFromWatcher();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (sender is FileSystemWatcher)
        {
            RequestRefreshFromWatcher();
        }
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (sender is FileSystemWatcher)
        {
            RequestRefreshFromWatcher();
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (sender is FileSystemWatcher)
        {
            RequestRefreshFromWatcher();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (sender is FileSystemWatcher)
        {
            RequestRefreshFromWatcher();
        }
    }

    private void RequestRefreshFromWatcher()
    {
        CancellationTokenSource cancellationTokenSource;
        lock (_refreshDebounceLock)
        {
            _refreshDebounceCancellationTokenSource?.Cancel();
            _refreshDebounceCancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource = _refreshDebounceCancellationTokenSource;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FileWatcherRefreshDelay, cancellationTokenSource.Token);
                if (!cancellationTokenSource.Token.IsCancellationRequested)
                {
                    // Forced: a listing already under way may have read the folder before this change
                    // landed, and a plain refresh is skipped while one is running, losing the change.
                    Refresh(forceRefresh: true);
                }
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                lock (_refreshDebounceLock)
                {
                    if (ReferenceEquals(_refreshDebounceCancellationTokenSource, cancellationTokenSource))
                    {
                        _refreshDebounceCancellationTokenSource = null;
                    }
                }

                cancellationTokenSource.Dispose();
            }
        });
    }

    public override void Delete(Url url, bool recursive, bool moveToRecycleBin)
    {
        Debug.Assert(!recursive || (url.PathType == PathType.Folder));
        switch (url.PathType)
        {
            case PathType.File:
                {
                    fileSystemService.DeleteFile(url, moveToRecycleBin);
                    break;
                }
            case PathType.Folder:
                {
                    fileSystemService.DeleteFolder(url, recursive, moveToRecycleBin);
                    break;
                }
            default:
                {
                    throw new NotImplementedException();
                }
        }
    }

    public override async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        await fileSystemService.MoveAsync(sourceUrl, targetUrl, overwrite);
    }

    public override async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        await fileSystemService.CopyAsync(sourceUrl, targetUrl, overwrite);
    }

    public override bool ExistsFile(Url url)
    {
        return File.Exists(url.Path);
    }

    public override bool ExistsFolder(Url url)
    {
        return Directory.Exists(url.Path);
    }

    public override async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        return await fileSystemService.ReadFileAsync(url);
    }

    public override async Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        await fileSystemService.WriteFileAsync(url, inputStream);
    }

    public override async Task CreateFolderAsync(Url url)
    {
        await fileSystemService.CreateFolderAsync(url);
    }

    public override Task RunTerminalCommandAsync(ITerminalModel terminalModel)
    {
        terminalModel.Command.EnvironmentVariables = string.Empty;
        var environmentVariables = Environment.GetEnvironmentVariables();
        var keys = environmentVariables.Keys.Cast<string>();
        foreach (var key in keys)
        {
            terminalModel.Command.EnvironmentVariables +=
                key + "=" + environmentVariables[key] + Environment.NewLine;
        }

        Process process = new();
        process.StartInfo.WorkingDirectory = terminalModel.Command.WorkingDirectoryUrl.Path;
        if (OperatingSystem.IsWindows()
            && terminalModel.Command.Command.EndsWith(".ps1", StringComparison.CurrentCultureIgnoreCase))
        {
            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = "-f " + terminalModel.Command.Command;
        }
        else if (OperatingSystem.IsWindows())
        {
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/C " + terminalModel.Command.Command;
        }
        else if (OperatingSystem.IsLinux()
                 && terminalModel.Command.Command.EndsWith(".ps1",
                     StringComparison.CurrentCultureIgnoreCase))
        {
            process.StartInfo.FileName = "powershell";
            process.StartInfo.Arguments = "-f " + terminalModel.Command.Command;
        }
        else if (OperatingSystem.IsLinux()
                 && terminalModel.Command.Command.EndsWith(".sh",
                     StringComparison.InvariantCultureIgnoreCase))
        {
            process.StartInfo.FileName = "bash";
            process.StartInfo.Arguments = "-c ./" + terminalModel.Command.Command;
        }
        else if (OperatingSystem.IsLinux())
        {
            process.StartInfo.FileName = terminalModel.Command.Command;
            process.StartInfo.Arguments = string.Empty;
        }

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardInput = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.Start();
        if (OperatingSystem.IsWindows())
        {
            terminalModel.Command.StartTime = process.StartTime;
        }

        process.WaitForExit();
        terminalModel.Command.ExitTime = process.ExitTime;
        terminalModel.Command.ExitCode = process.ExitCode;
        terminalModel.Command.Output = process.StandardOutput.ReadToEnd();
        terminalModel.Command.Error = process.StandardError.ReadToEnd();
        terminalModel.Command.Host = Environment.MachineName;
        terminalModel.Command.Username = Environment.UserName;

        return Task.CompletedTask;
    }
}
