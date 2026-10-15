namespace iTerminal.Core.Services.RuntimeDependencies;

public enum RuntimeDependencyState
{
    /// <summary>Nothing usable is installed. The feature that needs it stays off.</summary>
    Missing,

    /// <summary>The newest installed version is the newest one available on this line.</summary>
    Installed,

    /// <summary>A newer version on the same line is published than the one installed.</summary>
    UpdateAvailable,
}

/// <summary>
/// One dependency as the dialog and the start-up offer see it. Immutable: the service replaces the
/// whole list when something changes, so the render thread can read it without a lock.
/// </summary>
/// <param name="TargetVersion">What installing would put on disk: the newest known version on the build's line when missing or updating, null when nothing is to be done.</param>
/// <param name="Note">Something worth knowing that is not an action, e.g. a newer line that this build cannot use yet.</param>
/// <param name="Problem">Why the last check could not tell, e.g. no network.</param>
public sealed record RuntimeDependencyStatus(
    string Id,
    string Name,
    string Purpose,
    string? InstalledVersion,
    string? TargetVersion,
    RuntimeDependencyState State,
    string? Note = null,
    string? Problem = null);
