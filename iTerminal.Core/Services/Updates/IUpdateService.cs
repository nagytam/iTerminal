namespace iTerminal.Core.Services.Updates;

/// <summary>A newer iTerminal release than the one running.</summary>
/// <param name="Version">The release version, without the leading <c>v</c>.</param>
/// <param name="Installer">The Windows installer (MSI) of the release; null when it carries none.</param>
public sealed record UpdateInfo(string Version, RuntimeDependencies.GitHubAsset? Installer);

/// <summary>
/// Looks for a newer release of iTerminal itself on GitHub and, on Windows, downloads its installer
/// and starts it. Other systems are told where the release is; their packages are installed by hand.
/// </summary>
public interface IUpdateService
{
    /// <summary>The version of the running build.</summary>
    string CurrentVersion { get; }

    /// <summary>The newer release the last check found; null when there is none or no check has finished.</summary>
    UpdateInfo? Available { get; }

    /// <summary>True when the person skipped <see cref="Available"/>.</summary>
    bool IsSkipped { get; }

    /// <summary>Whether this system can download and start an installer itself.</summary>
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
    /// Downloads the installer of <see cref="Available"/>, checks it and starts it. True when the installer
    /// is running, which is the cue for the app to close so that its files can be replaced.
    /// </summary>
    Task<bool> InstallAsync(CancellationToken cancellationToken);
}
