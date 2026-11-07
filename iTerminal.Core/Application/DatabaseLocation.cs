using iTerminal.Core.Services;
using Microsoft.Data.Sqlite;

namespace iTerminal.Core.Application;

/// <summary>
/// Decides, once, where this process keeps its database. The first instance takes the lock next to
/// the database file and uses the file. Any later instance finds the lock taken and runs on a shared
/// in-memory database instead: two processes writing the same settings and caches would overwrite
/// each other, and a session that cannot save is better than one that corrupts the saved state.
/// </summary>
public sealed class DatabaseLocation(string databasePath, StartupWarningService warnings) : IDisposable
{
    private readonly object _gate = new();
    private SqliteConnection? _keepAlive;
    private string? _connectionString;

    /// <summary>The database file the first instance uses.</summary>
    public string DatabasePath { get; } = databasePath;

    /// <summary>The lock file whose holder owns <see cref="DatabasePath"/>.</summary>
    public string LockPath => DatabasePath + ".lock";

    /// <summary>True when another instance held the lock, so this one runs on an in-memory database.</summary>
    public bool IsInMemory { get; private set; }

    /// <summary>
    /// The SQLite connection string for this process. The first call decides file or memory (and adds
    /// the start-up warnings); every later call returns the same answer.
    /// </summary>
    public string ConnectionString
    {
        get
        {
            lock (_gate)
            {
                return _connectionString ??= Decide();
            }
        }
    }

    private string Decide()
    {
        if (warnings.TryAcquireLock(LockPath))
        {
            return $"Data Source={DatabasePath}";
        }

        IsInMemory = true;
        warnings.AddWarning("Another instance of iTerminal is already running.");
        warnings.AddWarning($"Locked database: {DatabasePath}");
        warnings.AddWarning("This instance uses a temporary in-memory database: its settings and caches are not saved.");

        // A named shared-cache memory database lives as long as one connection to it stays open, and
        // every context the factory makes needs to see the same one.
        string name = $"file:iTerminalMemory{Guid.NewGuid():N}?mode=memory&cache=shared";
        string connectionString = $"Data Source={name}";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        return connectionString;
    }

    /// <summary>
    /// Stops the other instance and removes its lock files. This instance stays on its in-memory
    /// database — its settings cache would overwrite the real ones if it switched — so the answer
    /// is a restart. Returns null on success, otherwise why it failed.
    /// </summary>
    public string? StopOtherInstance() => StartupWarningService.TryStopOtherInstance(LockPath);

    public void Dispose()
    {
        _keepAlive?.Dispose();
        _keepAlive = null;
    }
}
