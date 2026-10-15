namespace iTerminal.Core.Services.RuntimeDependencies;

public interface IRuntimeDependencyService
{
    /// <summary>Every dependency as it stands now. Replaced as a whole, never edited, so it is safe to read from the render thread.</summary>
    IReadOnlyList<RuntimeDependencyStatus> Statuses { get; }

    /// <summary>The most recent progress and error lines, oldest first.</summary>
    IReadOnlyList<string> Log { get; }

    /// <summary>True while an install is running.</summary>
    bool IsBusy { get; }

    /// <summary>True when something is missing or out of date and the person has not skipped that exact version.</summary>
    bool ShouldOfferAtStartup { get; }

    /// <summary>True when the dependency is installed, whether or not it is out of date.</summary>
    bool IsInstalled(string id);

    /// <summary>Asks the publisher for the newest versions. Never throws for a network failure: that becomes a <see cref="RuntimeDependencyStatus.Problem"/>.</summary>
    Task CheckAsync(CancellationToken cancellationToken);

    /// <summary>Installs what is missing or out of date, on a background task. Does nothing when an install is already running.</summary>
    void StartInstall();

    /// <summary>Installs only the dependencies named, on a background task. Ones that are up to date are left as they are.</summary>
    void StartInstall(IReadOnlyCollection<string>? ids);

    /// <summary>Installs what is missing or out of date and waits for it.</summary>
    Task InstallAsync(CancellationToken cancellationToken);

    /// <summary>Installs only the dependencies named and waits for it; null means every one that needs it.</summary>
    Task InstallAsync(CancellationToken cancellationToken, IReadOnlyCollection<string>? ids);

    /// <summary>Remembers that the currently offered versions are not wanted, so they are not offered at start-up again.</summary>
    void SkipOffers();

    /// <summary>Skips the offers of the dependencies named only; the others are offered again at the next start.</summary>
    void SkipOffers(IReadOnlyCollection<string>? ids);

    /// <summary>Marks a dependency's installed version incomplete, as a failed feature finds it to be.</summary>
    void InvalidateInstalled(string id);
}
