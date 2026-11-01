namespace iTerminal.Core.Services.BackgroundStart;

/// <summary>
/// Decides when a configurable background service starts. Each service's mode is a setting; a feature that needs a
/// service calls <see cref="Request"/> when it is opened, and the service waits in <see cref="WaitForStartAsync"/> until
/// its mode lets it begin.
/// </summary>
public interface IBackgroundStartService
{
    /// <summary>The mode the service is configured with. On demand when nothing valid is stored.</summary>
    BackgroundStartMode ModeOf(string serviceId);

    /// <summary>
    /// Asks for the service to start now. Idempotent; it starts the service in every mode, so a feature can call it
    /// whether or not the service is configured to wait.
    /// </summary>
    void Request(string serviceId);

    /// <summary>
    /// Completes when the service may start its first pass: at once when it is immediate; after the delay, or on a request,
    /// when it is delayed; on a request when it is on demand. Throws when <paramref name="cancellationToken"/> is cancelled first.
    /// </summary>
    Task WaitForStartAsync(string serviceId, CancellationToken cancellationToken);
}
