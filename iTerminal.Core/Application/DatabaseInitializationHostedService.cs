using iTerminal.Core.Data;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

// Clean up obsolete folder contents cache entries during DB initialization.

namespace iTerminal.Core.Application;

public class DatabaseInitializationHostedService(
    IDbContextFactory<iTerminalDbContext> dbContextFactory,
    StartupWarningService startupWarningService) : IHostedService
{
    /// <summary>
    /// Key of the schema stamp in <see cref="iTerminalDbContext.DatabaseMetadata"/>. That table
    /// exists for this one row — see <see cref="DatabaseMetadataEntry"/> for why the stamp cannot
    /// live in <c>Settings</c>.
    /// </summary>
    private const string ModelHashKey = "ModelHash";

    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory = dbContextFactory;
    private readonly StartupWarningService _startupWarningService = startupWarningService;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await TryAcquireInstanceLockAsync(cancellationToken);

        try
        {
            await InitializeCoreAsync(cancellationToken);
        }
        catch (Exception)
        {
            await RecreateAsync();
        }
    }

    private async Task TryAcquireInstanceLockAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var ctx = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var dbPath = GetDatabasePath(ctx);
            if (dbPath is null) return;

            var lockPath = dbPath + ".lock";
            if (!_startupWarningService.TryAcquireLock(lockPath))
            {
                _startupWarningService.AddWarning(
                    $"Another instance of iTerminal is already running.");
                _startupWarningService.AddWarning(
                    $"Locked database: {dbPath}");
            }
        }
        catch
        {
            // Never block startup due to lock check failures.
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        string currentModelHash = ComputeModelHash(context);

        bool created = await context.Database.EnsureCreatedAsync(cancellationToken);
        if (created)
        {
            context.DatabaseMetadata.Add(new DatabaseMetadataEntry { Key = ModelHashKey, Value = currentModelHash });
            await context.SaveChangesAsync(cancellationToken);
        }
        else if (await ReadModelHashAsync(context, cancellationToken) != currentModelHash)
        {
            // Pooled connections from services that have already read the database (settings, most
            // of all) keep the file open, and File.Delete then fails — which used to drop the whole
            // initialization into the blunt RecreateAsync path.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            await context.Database.EnsureDeletedAsync(cancellationToken);

            await using var recreateContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            await recreateContext.Database.EnsureCreatedAsync(cancellationToken);
            recreateContext.DatabaseMetadata.Add(new DatabaseMetadataEntry { Key = ModelHashKey, Value = currentModelHash });
            await recreateContext.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await using var cleanupContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var folderThreshold = DateTimeOffset.UtcNow - TimeSpan.FromDays(3);
            var obsoleteFolderEntries = cleanupContext.FolderContentsCacheEntries.Where(e => e.SavedAtUtc < folderThreshold);
            if (await obsoleteFolderEntries.AnyAsync(cancellationToken))
            {
                cleanupContext.FolderContentsCacheEntries.RemoveRange(obsoleteFolderEntries);
                await cleanupContext.SaveChangesAsync(cancellationToken);
            }

            var weatherThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(15);
            var obsoleteWeatherEntries = cleanupContext.CachedWeatherInfos.Where(w => w.CachedAtUtc < weatherThreshold);
            if (await obsoleteWeatherEntries.AnyAsync(cancellationToken))
            {
                await obsoleteWeatherEntries.ExecuteDeleteAsync(cancellationToken);
            }

            var instrumentThreshold = DateTime.UtcNow - TimeSpan.FromMinutes(5);
            var obsoleteInstrumentEntries = cleanupContext.CachedInstrumentValues.Where(i => i.CachedAtUtc < instrumentThreshold);
            if (await obsoleteInstrumentEntries.AnyAsync(cancellationToken))
            {
                await obsoleteInstrumentEntries.ExecuteDeleteAsync(cancellationToken);
            }
        }
        catch
        {
            // Swallow cleanup errors; DB remains usable.
        }
    }

    private async Task RecreateAsync()
    {
        try
        {
            // Read the file path from a fresh context, then dispose it completely before
            // touching the file so SQLite releases all file handles and WAL locks.
            string? dbPath;
            await using (var ctx = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None))
            {
                dbPath = GetDatabasePath(ctx);
            }

            // Release any pooled connections FIRST so SQLite drops its file handles/WAL locks;
            // otherwise File.Delete below throws (the corrupt DB stays in place) because a pooled
            // connection opened during the failed initialization still holds the file open.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            if (dbPath is not null)
            {
                foreach (var side in new[] { dbPath + "-wal", dbPath + "-shm" })
                    if (File.Exists(side)) File.Delete(side);
                if (File.Exists(dbPath)) File.Delete(dbPath);
            }

            await using var freshContext = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
            await freshContext.Database.EnsureCreatedAsync(CancellationToken.None);
            var hash = ComputeModelHash(freshContext);
            freshContext.DatabaseMetadata.Add(new DatabaseMetadataEntry { Key = ModelHashKey, Value = hash });
            await freshContext.SaveChangesAsync(CancellationToken.None);
        }
        catch
        {
            // If recreation also fails the app starts without DB persistence.
        }
    }

    /// <summary>
    /// The stamp the schema was created with, or null when there is none to read. A database written
    /// before the stamp had its own table has no <c>DatabaseMetadata</c> at all, and the query throws
    /// rather than returning nothing — which is the same answer as "unknown" and is treated as one.
    /// </summary>
    private static async Task<string?> ReadModelHashAsync(iTerminalDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            DatabaseMetadataEntry? stamp =
                await context.DatabaseMetadata.FindAsync(new object[] { ModelHashKey }, cancellationToken);
            return stamp?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetDatabasePath(DbContext context)
    {
        var cs = context.Database.GetConnectionString() ?? string.Empty;
        foreach (var part in cs.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static string ComputeModelHash(DbContext context)
    {
        var sb = new StringBuilder();
        foreach (var entityType in context.Model.GetEntityTypes().OrderBy(t => t.Name))
        {
            sb.Append(entityType.Name).Append('|');
            foreach (var prop in entityType.GetProperties().OrderBy(p => p.Name))
            {
                sb.Append(prop.Name).Append(':');
                sb.Append(prop.ClrType?.FullName ?? string.Empty).Append(',');
            }
            sb.Append(';');
        }

        using var sha = SHA256.Create();
        byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        var hashSb = new StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
            hashSb.Append(b.ToString("x2"));
        return hashSb.ToString();
    }
}
