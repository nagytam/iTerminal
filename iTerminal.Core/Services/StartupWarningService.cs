namespace iTerminal.Core.Services;

/// <summary>
/// Collects warnings that should be shown to the user immediately after the TUI starts.
/// Also owns the per-process lock file that prevents two instances from sharing the same database.
/// </summary>
public sealed class StartupWarningService : IDisposable
{
    private readonly List<string> _warnings = [];
    private FileStream? _lockStream;

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Tries to acquire an exclusive lock on <paramref name="lockPath"/>.
    /// Returns true when the lock was acquired (this is the only running instance).
    /// Returns false when another instance already holds the lock.
    /// </summary>
    public bool TryAcquireLock(string lockPath)
    {
        try
        {
            _lockStream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void AddWarning(string message) => _warnings.Add(message);

    public void Dispose()
    {
        _lockStream?.Dispose();
        _lockStream = null;
    }
}
