using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace iTerminal.Core.Tasks;

public class FilePanelRefreshTask : BackgroundTask
{
    private const string Key = nameof(FilePanelRefreshTask);
    private const int BatchSize = 50;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(3);
    // Keep prefetched folder listings warm long enough to be useful for navigation.
    // The MemoryCacheService default (5s) is far too short for prefetch to pay off.
    private static readonly TimeSpan MemoryCacheTtl = TimeSpan.FromMinutes(5);

    private readonly IMemoryCacheService _memoryCacheService;
    private readonly Serilog.ILogger _logger;
    private readonly IDbContextFactory<iTerminalDbContext>? _dbContextFactory;
    private readonly ContentModelFileSystem? _contentModel;
    private readonly IStopwatchFactory? _stopwatchFactory;

    public Url Url { get; }
    public string ContentModelName { get; }

    /// <summary>
    /// Per-phase durations recorded during the last <see cref="ExecuteAsync"/> call.
    /// Null when no <see cref="IStopwatchFactory"/> was injected.
    /// Keys: "CacheLookup", "Enumeration", "UiUpdate".
    /// </summary>
    public IReadOnlyDictionary<string, TimeSpan>? PhaseDurations { get; private set; }

    public override string DisplayName => $"{nameof(FilePanelRefreshTask)} {Url?.FullUrl}";

    // A task bound to a content model drives the visible panel, so it must run ahead of the
    // background-only prefetch tasks (parent/child folders). Lower enum value = higher priority.
    public override TaskPriority Priority =>
        _contentModel is not null ? TaskPriority.Input : TaskPriority.Background;

    public FilePanelRefreshTask(
        ContentModelFileSystem contentModel,
        IMemoryCacheService memoryCacheService,
        Serilog.ILogger logger,
        IDbContextFactory<iTerminalDbContext>? dbContextFactory = null,
        IStopwatchFactory? stopwatchFactory = null)
    {
        _contentModel = contentModel ?? throw new ArgumentNullException(nameof(contentModel));
        Url = contentModel.Url;
        ContentModelName = contentModel.Name;
        _memoryCacheService = memoryCacheService;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _stopwatchFactory = stopwatchFactory;
    }

    /// <summary>
    /// Construct a background-only cache task for a Url (no UI/content model updates).
    /// </summary>
    public FilePanelRefreshTask(
        Url url,
        string contentModelName,
        IMemoryCacheService memoryCacheService,
        Serilog.ILogger logger,
        IDbContextFactory<iTerminalDbContext>? dbContextFactory = null,
        IStopwatchFactory? stopwatchFactory = null)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        ContentModelName = contentModelName ?? string.Empty;
        _memoryCacheService = memoryCacheService;
        _logger = logger;
        _dbContextFactory = dbContextFactory;
        _stopwatchFactory = stopwatchFactory;
    }

    public override async Task ExecuteAsync()
    {
        _logger.Information(nameof(FilePanelRefreshTask) + $" {Url.FullUrl} started");

        var key = Key + Url.FullUrl;
        var phases = _stopwatchFactory is not null ? new Dictionary<string, TimeSpan>() : null;

        // ── Phase: cache lookup ────────────────────────────────────────────────
        // Fast path: populate memory cache from existing in-memory listing or persisted DB so
        // that subsequent switches to this folder are instant. UI updates are only performed
        // when a content model instance was provided and matches the Url.
        var cacheSw = _stopwatchFactory?.StartNew();
        try
        {
            if (!CancellationToken.IsCancellationRequested)
            {
                if (_memoryCacheService.Get(key) is List<IEntry> cached)
                {
                    if (_contentModel is not null && IsCurrentUrl())
                    {
                        ReplaceEntriesAndRestoreFocus(cached, isComplete: true, isFromCache: true);
                    }
                }
                else if (_dbContextFactory is not null)
                {
                    try
                    {
                        using var ctx = _dbContextFactory.CreateDbContext();
                        var entity = ctx.FolderContentsCacheEntries.Find(Url.Path);
                        if (entity is not null)
                        {
                            var age = DateTimeOffset.UtcNow - entity.SavedAtUtc;
                            if (age <= CacheTtl && !string.IsNullOrEmpty(entity.ContentJson))
                            {
                                var dtos = JsonSerializer.Deserialize<List<CachedEntryDto>>(entity.ContentJson) ?? new();
                                var list = dtos.Select(d => new FileSystemEntry(d.Name, d.FullUrl)).Cast<IEntry>().ToList();
                                _memoryCacheService.Set(key, list, MemoryCacheTtl);
                                if (_contentModel is not null && IsCurrentUrl())
                                {
                                    ReplaceEntriesAndRestoreFocus(list, isComplete: true, isFromCache: true);
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            cacheSw?.Stop();
            if (phases is not null && cacheSw is not null)
                phases["CacheLookup"] = cacheSw.Elapsed;
        }

        // ── Phase: cold enumeration ────────────────────────────────────────────
        List<IEntry>? entries = null;
        var enumSw = _stopwatchFactory?.StartNew();
        try
        {
            entries = await StreamEntriesAsync();
            enumSw?.Stop();
            if (phases is not null && enumSw is not null)
                phases["Enumeration"] = enumSw.Elapsed;

            // A cancelled enumeration returns the entries it had read, which is not the folder. Caching it
            // would serve a partial listing as complete to the next panel (and to the next start-up), so
            // nothing is cached or shown; the refresh that replaced this one reads the whole folder.
            if (CancellationToken.IsCancellationRequested)
            {
                PhaseDurations = phases;
                return;
            }

            _memoryCacheService.Set(key, entries, MemoryCacheTtl);

            // ── Phase: UI update ───────────────────────────────────────────────
            if (!CancellationToken.IsCancellationRequested && _contentModel is not null && IsCurrentUrl())
            {
                var uiSw = _stopwatchFactory?.StartNew();
                ReplaceEntriesAndRestoreFocus(entries, isComplete: true);
                uiSw?.Stop();
                if (phases is not null && uiSw is not null)
                    phases["UiUpdate"] = uiSw.Elapsed;
            }

            // Persist warm cache to DB
            if (_dbContextFactory is not null)
            {
                try
                {
                    using var ctx = _dbContextFactory.CreateDbContext();
                    ctx.Database.EnsureCreated();
                    var dtos = entries.Select(e => new CachedEntryDto(e is FileSystemEntry f ? f.Url.DisplayName : e.Url.DisplayName, e.Url.FullUrl)).ToList();
                    var json = JsonSerializer.Serialize(dtos);
                    var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    ctx.Database.ExecuteSqlInterpolated($@"
INSERT INTO FolderContentsCache(Path, SavedAtUtc, ContentJson)
VALUES ({Url.Path}, {nowMs}, {json})
ON CONFLICT(Path) DO UPDATE SET
  SavedAtUtc = excluded.SavedAtUtc,
  ContentJson = excluded.ContentJson;
                    ");
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        if (!CancellationToken.IsCancellationRequested && _contentModel is not null && IsCurrentUrl())
        {
            try
            {
                _contentModel.Update();
            }
            catch
            {
            }
        }

        PhaseDurations = phases;

        var finishedCount = entries is not null ? entries.Count : (_contentModel is not null ? _contentModel.Entries.Count : 0);
        _logger.Information(nameof(FilePanelRefreshTask) + $" {Url.FullUrl} with {finishedCount} entries finished");
    }

    /// <summary>
    /// Enumerates the directory in batches, keeping folders and files in separate lists so
    /// the final merge+sort is O(n log n) rather than O(n² log n). The first batch is pushed
    /// to the UI as soon as it is ready so the panel is never blank waiting for a large
    /// directory to finish loading. Intermediate batch updates show folders before files but
    /// without intra-group sorting; the final push is fully sorted.
    /// </summary>
    private async Task<List<IEntry>> StreamEntriesAsync()
    {
        var hasParent = Url.Path != Path.GetPathRoot(Url.Path);
        IEntry? parentEntry = hasParent ? new FileSystemEntry("..", Url.ParentPath) : null;

        var folders = new List<IEntry>();
        var files = new List<IEntry>();
        int total = 0;

        var dir = new DirectoryInfo(Url.Path);
        foreach (var info in dir.EnumerateFileSystemInfos())
        {
            if (CancellationToken.IsCancellationRequested)
            {
                break;
            }

            var entry = new FileSystemEntry(info);
            if (info is DirectoryInfo) folders.Add(entry);
            else files.Add(entry);
            total++;

            if (total % BatchSize == 0)
            {
                // Intermediate push: folders before files, unsorted within each group.
                // Sorting once at the end avoids O(n² log n) work across all batches.
                if (!CancellationToken.IsCancellationRequested && _contentModel is not null && IsCurrentUrl())
                {
                    ReplaceEntriesAndRestoreFocus(MergeSnapshot(parentEntry, folders, files), isComplete: false);
                }
                await Task.Yield();
            }
        }

        // Sort each group once and build the final result.
        folders.Sort(NameComparer.Instance);
        files.Sort(NameComparer.Instance);

        var result = new List<IEntry>(
            (parentEntry is not null ? 1 : 0) + folders.Count + files.Count);
        if (parentEntry is not null) result.Add(parentEntry);
        result.AddRange(folders);
        result.AddRange(files);
        return result;
    }

    private static List<IEntry> MergeSnapshot(IEntry? parentEntry, List<IEntry> folders, List<IEntry> files)
    {
        var snapshot = new List<IEntry>(
            (parentEntry is not null ? 1 : 0) + folders.Count + files.Count);
        if (parentEntry is not null) snapshot.Add(parentEntry);
        snapshot.AddRange(folders);
        snapshot.AddRange(files);
        return snapshot;
    }

    /// <param name="isComplete">True for the whole folder, false for a streamed batch; see <see cref="ContentModelBase.ReplaceEntriesAndRestoreFocus"/>.</param>
    private void ReplaceEntriesAndRestoreFocus(List<IEntry> entries, bool isComplete, bool isFromCache = false) =>
        _contentModel?.ReplaceEntriesAndRestoreFocus(entries, isComplete, isFromCache);

    private bool IsCurrentUrl() =>
        _contentModel is not null && string.Equals(_contentModel.Url.FullUrl, Url.FullUrl, StringComparison.Ordinal);

    private sealed record CachedEntryDto(string Name, string FullUrl);

    private sealed class NameComparer : IComparer<IEntry>
    {
        public static readonly NameComparer Instance = new();

        public int Compare(IEntry? x, IEntry? y)
        {
            if (x is null) return y is null ? 0 : -1;
            if (y is null) return 1;
            return string.Compare(x.Url.DisplayName, y.Url.DisplayName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
