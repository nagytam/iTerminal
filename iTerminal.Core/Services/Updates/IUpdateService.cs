namespace iTerminal.Core.Services.Updates;

/// <summary>A newer iTerminal release than the one running.</summary>
/// <param name="Version">The release version, without the leading <c>v</c>.</param>
/// <param name="Installer">The download this copy of iTerminal installs: the MSI, or the portable archive; null when the release carries none.</param>
public sealed record UpdateInfo(string Version, RuntimeDependencies.GitHubAsset? Installer);

/// <summary>How the running copy of iTerminal was installed, and therefore what an update downloads.</summary>
public enum UpdateInstallKind
{
    /// <summary>Not a portable copy outside Windows (a Linux package): the release is only announced.</summary>
    Unsupported,

    /// <summary>Installed by the Windows installer (MSI): the release's <c>.msi</c> runs with msiexec.</summary>
    Installer,

    /// <summary>
    /// An unzipped portable copy (it carries the portable marker file) on Windows, Linux or macOS: the release's portable
    /// archive (zip, or tar.gz on Linux and macOS) replaces its files.
    /// </summary>
    PortableArchive,

    /// <summary>
    /// Installed by WinGet (the MSI, with the WinGet marker in the registry). WinGet owns the install and its updates,
    /// so this copy does not install a release itself; it says to run <c>winget upgrade</c>.
    /// </summary>
    PackageManager,
}

/// <summary>
/// Looks for a newer release of iTerminal itself on GitHub and downloads the release for the way it was installed
/// (the MSI on Windows, or the portable archive of a portable copy on Windows or Linux) and applies it. Other copies
/// (Linux packages) are told where the release is; their packages are installed by hand.
/// </summary>
public interface IUpdateService
{
    /// <summary>The version of the running build.</summary>
    string CurrentVersion { get; }

    /// <summary>The newer release the last check found; null when there is none or no check has finished.</summary>
    UpdateInfo? Available { get; }

    /// <summary>True when the person skipped <see cref="Available"/>.</summary>
    bool IsSkipped { get; }

    /// <summary>How this copy was installed; decides which download an update fetches.</summary>
    UpdateInstallKind InstallKind { get; }

    /// <summary>Whether this system can download and apply an update itself.</summary>
    bool CanInstall { get; }

    bool IsBusy { get; }

    /// <summary>What the download and install did so far, oldest first.</summary>
    IReadOnlyList<string> Log { get; }

    /// <summary>The page the releases are listed on.</summary>
    string ReleasesAddress { get; }

    /// <summary>Asks GitHub for the newest release. A failure (no network) is not an error: nothing is offered.</summary>
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Hides <see cref="Available"/> until a newer release is published.</summary>
    void Skip();

    /// <summary>
    /// Downloads the release of <see cref="Available"/>, checks it and starts its installer or replacement script.
    /// True when that is running, which is the cue for the app to close so that its files can be replaced.
    /// </summary>
    Task<bool> InstallAsync(CancellationToken cancellationToken);
}
