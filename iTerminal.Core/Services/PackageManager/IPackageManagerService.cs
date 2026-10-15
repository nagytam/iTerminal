using iTerminal.Core.Model.PackageManager;

namespace iTerminal.Core.Services.PackageManager;

/// <summary>
/// Async, structured access to the host's package manager (winget on Windows, APT on Debian/Ubuntu
/// Linux, ...). Every member runs the underlying CLI off the UI threads; callers must never await
/// these from a render/draw path.
/// </summary>
public interface IPackageManagerService
{
    /// <summary>Short name shown in the UI (e.g. "WinGet", "APT").</summary>
    string DisplayName { get; }

    /// <summary>Packages currently installed on the machine.</summary>
    Task<PackageQueryResult> GetInstalledPackagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Packages available from configured sources matching <paramref name="query"/>.</summary>
    Task<PackageQueryResult> SearchPackagesAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs <paramref name="packageId"/>, streaming each line of stdout/stderr to
    /// <paramref name="onOutputLine"/> as it is produced. Returns the process exit code, or a
    /// negative value if it could not run or was cancelled.
    /// </summary>
    Task<int> InstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstalls <paramref name="packageId"/>, streaming each line of stdout/stderr to
    /// <paramref name="onOutputLine"/> as it is produced. Returns the process exit code, or a
    /// negative value if it could not run or was cancelled.
    /// </summary>
    Task<int> UninstallAsync(string packageId, Action<string> onOutputLine, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a package manager list/search query.</summary>
public sealed record PackageQueryResult(bool Success, IReadOnlyList<PackageInfo> Packages, string Error)
{
    public static PackageQueryResult Ok(IReadOnlyList<PackageInfo> packages) => new(true, packages, string.Empty);
    public static PackageQueryResult Fail(string error) => new(false, [], error);
}
