namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// A component iTerminal needs at run time but does not ship. It is downloaded into the data folder
/// by the setup dialog, and found there again on the next start.
/// </summary>
public interface IRuntimeDependency
{
    /// <summary>A stable key for settings and the status list, e.g. <c>playwright</c>.</summary>
    string Id { get; }

    string Name { get; }

    /// <summary>What the application needs it for, in a few words; shown in the dialog.</summary>
    string Purpose { get; }

    /// <summary>
    /// The version this build was compiled against. It is the install target until a check has found a
    /// newer version on the same line (see <see cref="IsCompatible"/>), so the code and the component agree.
    /// </summary>
    string ExpectedVersion { get; }

    /// <summary>
    /// Whether a version can be used with this build. An update is only offered within the line of
    /// <see cref="ExpectedVersion"/>: a newer line may change what the code talks to, and needs a
    /// build against it rather than a download.
    /// </summary>
    bool IsCompatible(Version version);

    /// <summary>The newest complete installed version, or null when there is none.</summary>
    string? InstalledVersion();

    /// <summary>Every version the publisher offers, as published (stable and pre-release alike).</summary>
    Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and sets up one version, then makes it the one that is used.</summary>
    Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken);

    /// <summary>Points the process at the installed version. Does nothing when none is installed.</summary>
    void Activate();

    /// <summary>Marks the installed versions incomplete, so they are reported missing and installed again.</summary>
    void Invalidate();
}
