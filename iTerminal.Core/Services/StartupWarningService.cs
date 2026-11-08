using iConsole.Localization;
using System.Diagnostics;

namespace iTerminal.Core.Services;

/// <summary>
/// Collects warnings that should be shown to the user immediately after the TUI starts.
/// Also owns the per-process lock file that prevents two instances from sharing the same database.
/// </summary>
public sealed class StartupWarningService : IDisposable
{
    private readonly List<string> _warnings = [];
    private FileStream? _lockStream;
    private string? _pidPath;

    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Extension of the sidecar file next to the lock that names the process holding it.</summary>
    public const string PidFileSuffix = ".pid";

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
        }
        catch (IOException)
        {
            return false;
        }

        // The lock itself cannot be read by the instance that lost it (FileShare.None), so the owner's
        // process id goes into a sidecar file: that is what lets a second instance offer to stop it.
        try
        {
            _pidPath = lockPath + PidFileSuffix;
            File.WriteAllText(_pidPath, Environment.ProcessId.ToString());
        }
        catch
        {
            _pidPath = null;
        }

        return true;
    }

    public void AddWarning(string message) => _warnings.Add(message);

    /// <summary>
    /// Stops the instance that holds <paramref name="lockPath"/> and removes the lock files, so the next
    /// start can take the database. Only a process with this program's own name is ever killed — a stale
    /// pid file may name a process that has since been reused by something unrelated.
    /// Returns null on success, otherwise a sentence saying why it did not work.
    /// </summary>
    public static string? TryStopOtherInstance(string lockPath)
    {
        string pidPath = lockPath + PidFileSuffix;
        try
        {
            if (!File.Exists(pidPath) || !int.TryParse(File.ReadAllText(pidPath).Trim(), out int pid))
            {
                return UiText.Get("Startup.NoPid", "The other instance could not be identified (no process id recorded).");
            }

            if (pid == Environment.ProcessId)
            {
                return UiText.Get("Startup.OwnLock", "The lock belongs to this instance.");
            }

            try
            {
                using Process other = Process.GetProcessById(pid);
                using Process self = Process.GetCurrentProcess();
                if (!string.Equals(other.ProcessName, self.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Format(UiText.Get("Startup.NotITerminal", "Process {0} is not an iTerminal instance ({1}); not stopping it."), pid, other.ProcessName);
                }

                other.Kill();
                if (!other.WaitForExit(5000))
                {
                    return string.Format(UiText.Get("Startup.DidNotExit", "Process {0} did not exit."), pid);
                }
            }
            catch (ArgumentException)
            {
                // Not running any more: only the lock file is left over.
            }

            // The OS releases the dead process's handle a moment after it exits.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    if (File.Exists(lockPath)) File.Delete(lockPath);
                    if (File.Exists(pidPath)) File.Delete(pidPath);
                    return null;
                }
                catch (IOException)
                {
                    Thread.Sleep(100);
                }
            }

            return UiText.Get("Startup.LockInUse", "The lock file is still in use and could not be removed.");
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void Dispose()
    {
        _lockStream?.Dispose();
        _lockStream = null;
        if (_pidPath is not null)
        {
            try { File.Delete(_pidPath); } catch { }
            _pidPath = null;
        }
    }
}
