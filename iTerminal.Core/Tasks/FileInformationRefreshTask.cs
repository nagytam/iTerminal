using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Services;
using Microsoft.Extensions.Options;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace iTerminal.Core.Tasks;

public class FileInformationRefreshTask 
    : BackgroundTask
{
    private const string EnumerateFilesCacheKeyPrefix = nameof(FileInformationRefreshTask) + ".EnumerateFiles.";
    // Increased cache duration to reduce repeated directory enumeration for short-lived bursts.
    private static readonly TimeSpan EnumerateFilesCacheDuration = TimeSpan.FromSeconds(5);
    public override string DisplayName => $"{nameof(FileInformationRefreshTask)} {CurrentEntry?.Url?.FullUrl}";
    public ConcurrentDictionary<string, Tuple<IEntry, IContentModel>> Entries { get; private set; } = [];
    private IEntry CurrentEntry { get; set; } = null!;
    private IContentModel CurrentModel { get; set; } = null!;
    private ILogger Logger { get; set; }
    private IMemoryCacheService MemoryCacheService { get; }
    private DateTime _lastModelProgressUpdate = DateTime.MinValue;
    private static readonly TimeSpan ModelProgressUpdateInterval = TimeSpan.FromMilliseconds(500);
    private readonly uint _reportBatchSize;
    private readonly object _countLock = new();
    private bool _countFinished;

    public FileInformationRefreshTask(
        IEntry entry, 
        IContentModel? contentModel,
        IMemoryCacheService memoryCacheService,
        ILogger logger,
        uint reportBatchSize = 50)
        : base()
    {
        MemoryCacheService = memoryCacheService;
        Logger = logger;
        _reportBatchSize = reportBatchSize;
        Name = $"{Name} {Entries.Count}";
        AddEntry(entry, contentModel);
    }

    public FileInformationRefreshTask(
        IEntry entry,
        IContentModel? contentModel,
        IMemoryCacheService memoryCacheService,
        ILogger logger,
        IOptions<FileInformationRefreshOptions> options)
        : this(entry, contentModel, memoryCacheService, logger, options?.Value?.ReportBatchSize ?? 50u)
    {
    }

    public override Task ExecuteAsync()
    {
        Logger.Information(nameof(FileInformationRefreshTask) + " started");
        while (!Entries.IsEmpty && !CancellationToken.IsCancellationRequested)
        {
            var item = Entries.FirstOrDefault();
            if (item.Key is not null)
            {
                Logger.Information(nameof(FileInformationRefreshTask) + $" refresh {item.Key} ({Entries.Count})");
                try
                {
                    CurrentEntry = item.Value.Item1;
                    CurrentModel = item.Value.Item2;
                    if (CurrentEntry.Url.PathType == PathType.Folder &&
                        CurrentEntry.Url.UrlScheme == Container.FILE)
                    {
                        uint numberOfFiles = 0;
                        lock (_countLock)
                        {
                            _countFinished = false;
                        }
                        Progress.Report(numberOfFiles);
                        var counted = GetNumberOfFiles(ref numberOfFiles, CurrentEntry.FullName, CancellationToken);
                        lock (_countLock)
                        {
                            // Set before the final value, so a progress report that is delivered late cannot put "…" back.
                            _countFinished = true;
                            if (!CancellationToken.IsCancellationRequested)
                            {
                                // A count that stopped part-way (a folder that cannot be read) is not a total: say so, not a short number.
                                CurrentEntry.NumberOfFiles = counted ? numberOfFiles.ToString("N0") : "?";
                            }
                        }
                    }
                }
                catch (Exception exception)
                {
                    Logger.Warning(exception, nameof(FileInformationRefreshTask) + " could not count {Url}", item.Value.Item1.Url?.FullUrl);
                }
                finally
                {
                    Entries.TryRemove(item);
                    if (!CancellationToken.IsCancellationRequested)
                    {
                        CurrentModel?.Update();
                    }
                }
            }
        }
        Logger.Information(nameof(FileInformationRefreshTask) + " finished");
        return Task.CompletedTask;
    }

    protected override void OnProgressChanged(long value)
    {
        base.OnProgressChanged(value);

        lock (_countLock)
        {
            // Progress is delivered on a later turn than it was reported, so one can arrive after the count has finished.
            if (_countFinished || CancellationToken.IsCancellationRequested)
            {
                return;
            }

            // The running count, not a spinner: a large tree takes a while, and a number that
            // climbs says both that work is happening and roughly how much there is.
            CurrentEntry.NumberOfFiles = $"{value:N0}…";
        }

        var now = DateTime.UtcNow;
        if (now - _lastModelProgressUpdate >= ModelProgressUpdateInterval)
        {
            _lastModelProgressUpdate = now;
            CurrentModel?.Update();
        }
    }

    /// <returns>Whether the count ran to the end. False when the folder, or something under it, could not be read.</returns>
    private bool GetNumberOfFiles(ref uint numberOfFiles, string fullName, CancellationToken cancellationToken)
    {
        try
        {
            fullName = Path.GetFullPath(fullName);
            // Batch progress reports to reduce frequent UI updates and allocations.
            // Report only every reportBatchSize files and once at the end.
            uint lastReported = numberOfFiles;
            GetNumberOfFilesCore(ref numberOfFiles, fullName, cancellationToken, ref lastReported, _reportBatchSize);

            if (!cancellationToken.IsCancellationRequested)
            {
                // Ensure final count is reported.
                Progress.Report(numberOfFiles);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Logger.Debug(exception, nameof(FileInformationRefreshTask) + " could not read {Folder}", fullName);
            return false;
        }
    }

    private void GetNumberOfFilesCore(ref uint numberOfFiles, string fullName, CancellationToken cancellationToken, ref uint lastReported, uint reportBatchSize)
    {
        foreach (string file in GetFiles(fullName))
        {
            _ = file;
            if (cancellationToken.IsCancellationRequested)
            {
                numberOfFiles = 0;
                return;
            }

            numberOfFiles++;
            if (numberOfFiles - lastReported >= reportBatchSize)
            {
                lastReported = numberOfFiles;
                Progress.Report(numberOfFiles);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(fullName))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                numberOfFiles = 0;
                return;
            }

            // Not through a link: its files are another folder's, and a link to a parent would
            // have the count go round for ever.
            if (iConsole.Helper.FileLinkHelper.IsLink(directory))
            {
                continue;
            }

            GetNumberOfFilesCore(ref numberOfFiles, directory, cancellationToken, ref lastReported, reportBatchSize);
        }
    }

    private IReadOnlyList<string> GetFiles(string fullName)
    {
        string cacheKey = EnumerateFilesCacheKeyPrefix + fullName;
        if (MemoryCacheService.Get(cacheKey) is IReadOnlyList<string> cachedFiles)
        {
            return cachedFiles;
        }

        List<string> files = Directory.EnumerateFiles(fullName).ToList();
        MemoryCacheService.Set(cacheKey, files, EnumerateFilesCacheDuration);
        return files;
    }

    public void AddEntry(IEntry entry, IContentModel? contentModel)
    {
        if (contentModel is null)
        {
            return;
        }

        var key = entry.Url.FullUrl;
        if (!Entries.ContainsKey(key))
        {
            Debug.WriteLine("Refresh: " + key);
            Entries[key] = new Tuple<IEntry, IContentModel>(entry, contentModel);
        }
    }
}
