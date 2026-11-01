using iConsole.Data;
using iConsole.Jobs;
using iConsole.Model;
using iTerminal.Core.Data;
using iTerminal.Core.Model;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Collections.Specialized;

namespace iTerminal.Core.Jobs;

public class FolderIndexJob : Job
{
    private readonly FolderIndexModel _folderIndexModel;
    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory;
    private readonly ConcurrentQueue<IHierarchicalItemModel> _queue = [];
    private readonly Dictionary<string, FolderIndexCacheEntry> _cacheEntries = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan ScanThrottleDelay = TimeSpan.FromMilliseconds(100);

    public FolderIndexJob(
        FolderIndexModel folderIndexModel,
        DriveInfoModel driveInfoModel,
        IDbContextFactory<iTerminalDbContext> dbContextFactory)
    {
        _folderIndexModel = folderIndexModel ?? new FolderIndexModel();
        driveInfoModel ??= new DriveInfoModel();
        _dbContextFactory = dbContextFactory ?? new TemporaryDbContextFactory();

        driveInfoModel.Drives.CollectionChanged += OnDriveCollectionChanged;
        foreach (Drive driveInfo in driveInfoModel.Drives.AsSnapshot())
        {
            if (driveInfo is not null && driveInfo.DriveInfo is not null && driveInfo.DriveInfo.IsReady)
            {
                Folder child = new(_folderIndexModel, driveInfo.DriveInfo.Name);
                _queue.Enqueue(child);
                _folderIndexModel.Children.Add(child);
            }
        }
    }

    private void LoadCacheEntries()
    {
        using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();
        foreach (FolderIndexCacheEntry cacheEntry in context.FolderIndexCacheEntries.AsNoTracking())
        {
            _cacheEntries[cacheEntry.Path] = new FolderIndexCacheEntry
            {
                Path = cacheEntry.Path,
                Size = cacheEntry.Size,
                ModifiedUtc = cacheEntry.ModifiedUtc,
            };
        }
    }

    private void OnDriveCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (Data.Drive item in e.NewItems.Cast<Data.Drive>())
            {
                if (item.IsEnabled)
                {
                    Folder child = new(_folderIndexModel, item.Url);
                    _folderIndexModel.Children.Add(child);
                    _queue.Enqueue(child);
                }
            }
        }
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        // Nothing is walked until the folder selector asks for the index: it walks every folder on every drive, and
        // started at start-up it was the largest single source of processor load in the first 30 seconds. The cache
        // is read only then too, which also takes its read off the start-up path.
        await _folderIndexModel.IndexingRequested.WaitAsync(cancellationToken);
        LoadCacheEntries();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryDequeue(out IHierarchicalItemModel? folder) && folder is Folder currentFolder && currentFolder.Url is Url url)
                {
                    if (url.UrlScheme == Container.FILE)
                    {
                        if (TryGetFolderMetadata(url.Path, out long size, out DateTimeOffset modifiedUtc))
                        {
                            UpdateCacheIfChanged(url.Path, size, modifiedUtc);
                        }

                        string[] subFolderNames = Directory.GetDirectories(url.Path);
                        foreach (string subFolderName in subFolderNames)
                        {
                            Folder subFolder = new(currentFolder, subFolderName);
                            folder.Children.Add(subFolder);
                            _queue.Enqueue(subFolder);
                        }
                    }
                }
            }
            catch
            {
            }

            if (!cancellationToken.IsCancellationRequested && _queue.IsEmpty)
            {
                // After each full indexing pass, add a cooperative throttle delay:
                await Task.Delay(ScanThrottleDelay, cancellationToken);
            }
        }
    }

    private static bool TryGetFolderMetadata(string folderPath, out long size, out DateTimeOffset modifiedUtc)
    {
        size = 0;
        modifiedUtc = default;

        try
        {
            DirectoryInfo directoryInfo = new(folderPath);
            if (!directoryInfo.Exists)
            {
                return false;
            }

            modifiedUtc = directoryInfo.LastWriteTimeUtc;
            foreach (string file in Directory.EnumerateFiles(folderPath))
            {
                try
                {
                    FileInfo fileInfo = new(file);
                    size += fileInfo.Length;
                }
                catch
                {
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateCacheIfChanged(string folderPath, long size, DateTimeOffset modifiedUtc)
    {
        if (_cacheEntries.TryGetValue(folderPath, out FolderIndexCacheEntry? cacheEntry) &&
            cacheEntry.Size == size &&
            cacheEntry.ModifiedUtc == modifiedUtc)
        {
            return;
        }

        _cacheEntries[folderPath] = new FolderIndexCacheEntry
        {
            Path = folderPath,
            Size = size,
            ModifiedUtc = modifiedUtc,
        };

        using iTerminalDbContext context = _dbContextFactory.CreateDbContext();
        context.Database.EnsureCreated();

        FolderIndexCacheEntry? existingEntity = context.FolderIndexCacheEntries.Find(folderPath);
        if (existingEntity is null)
        {
            context.FolderIndexCacheEntries.Add(new FolderIndexCacheEntry
            {
                Path = folderPath,
                Size = size,
                ModifiedUtc = modifiedUtc,
            });
        }
        else
        {
            existingEntity.Size = size;
            existingEntity.ModifiedUtc = modifiedUtc;
        }

        context.SaveChanges();
    }

    public override JobPriority Priority => JobPriority.Background;

    private sealed class TemporaryDbContextFactory : IDbContextFactory<iTerminalDbContext>
    {
        private readonly DbContextOptions<iTerminalDbContext> _options;

        public TemporaryDbContextFactory()
        {
            string databasePath = Path.Combine(Path.GetTempPath(), $"iTerminal.FolderIndex.{Guid.NewGuid():N}.sqlite");
            DbContextOptionsBuilder<iTerminalDbContext> builder = new();
            builder.UseSqlite($"Data Source={databasePath}");
            _options = builder.Options;
        }

        public iTerminalDbContext CreateDbContext()
        {
            return new iTerminalDbContext(_options);
        }

        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CreateDbContext());
        }
    }
}
