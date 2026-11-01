using System.Collections.Concurrent;
using iTerminal.Core.Data;

namespace iTerminal.Core.Services.BackgroundStart;

/// <summary>
/// Reads each service's mode from its setting (<c>BackgroundStart.&lt;id&gt;</c>) and holds the requests that start the
/// on-demand and delayed services. The settings have no change notification, so a mode is read when a service
/// starts: a changed mode takes effect the next time the application starts.
/// </summary>
public sealed class BackgroundStartService(ISettingsService settingsService, TimeSpan? delayedStart = null) : IBackgroundStartService
{
    /// <summary>How long a delayed service waits after start-up when no feature has asked for it sooner.</summary>
    public static readonly TimeSpan DelayedStart = TimeSpan.FromSeconds(30);

    /// <summary>The mode used when a setting is missing or not one of the three.</summary>
    public const BackgroundStartMode DefaultMode = BackgroundStartMode.OnDemand;

    private readonly ConcurrentDictionary<string, TaskCompletionSource> _requests = new(StringComparer.Ordinal);
    private readonly TimeSpan _delayedStart = delayedStart ?? DelayedStart;

    /// <summary>The setting a service's mode is stored under.</summary>
    public static string SettingKey(string serviceId) => Settings.BackgroundStartPrefix + serviceId;

    /// <summary>
    /// The mode of a service whose setting is missing. The update check is delayed: nothing in the app opens it
    /// but About, so on demand it would never offer an update on its own.
    /// </summary>
    public static BackgroundStartMode DefaultModeOf(string? serviceId) =>
        serviceId == BackgroundServices.UpdateCheck ? BackgroundStartMode.Delayed : DefaultMode;

    /// <summary>Parses a stored mode, ignoring case. Anything else gives the service's default.</summary>
    public static BackgroundStartMode ParseMode(string? stored, string? serviceId = null) =>
        Enum.TryParse(stored, ignoreCase: true, out BackgroundStartMode mode) && Enum.IsDefined(mode)
            ? mode
            : DefaultModeOf(serviceId);

    public BackgroundStartMode ModeOf(string serviceId) => ParseMode(settingsService.Get(SettingKey(serviceId)), serviceId);

    public void Request(string serviceId) => Gate(serviceId).TrySetResult();

    public async Task WaitForStartAsync(string serviceId, CancellationToken cancellationToken)
    {
        switch (ModeOf(serviceId))
        {
            case BackgroundStartMode.Immediately:
                return;

            case BackgroundStartMode.Delayed:
                // Whichever comes first: the delay, or a feature asking for the service.
                Task delay = Task.Delay(_delayedStart, cancellationToken);
                await Task.WhenAny(delay, Gate(serviceId).Task);
                cancellationToken.ThrowIfCancellationRequested();
                return;

            default:
                await Gate(serviceId).Task.WaitAsync(cancellationToken);
                return;
        }
    }

    private TaskCompletionSource Gate(string serviceId) =>
        _requests.GetOrAdd(serviceId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}
