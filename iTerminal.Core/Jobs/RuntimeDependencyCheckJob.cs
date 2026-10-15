using iConsole.Jobs;
using iConsole.Services;
using iTerminal.Core.Services;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Asks the publishers of the runtime dependencies for their newest versions, a little after start-up and
/// then every <see cref="Interval"/>. An update is announced once, by a notification; the offer at start-up
/// and the dialog under Tools show it until it is installed or skipped.
/// </summary>
public class RuntimeDependencyCheckJob(
    IRuntimeDependencyService runtimeDependencies,
    INotificationService notifications,
    ISettingsService settings) : Job
{
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(StartDelay, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            await runtimeDependencies.CheckAsync(cancellationToken);
            AnnounceUpdates();
            await Task.Delay(Interval, cancellationToken);
        }
    }

    internal void AnnounceUpdates()
    {
        bool changed = false;
        foreach (RuntimeDependencyStatus status in runtimeDependencies.Statuses)
        {
            if (status.State != RuntimeDependencyState.UpdateAvailable || status.TargetVersion is null)
            {
                continue;
            }

            string key = RuntimeDependencyService.AnnouncedSettingPrefix + status.Id;
            if (settings.Get(key) == status.TargetVersion)
            {
                continue;
            }

            notifications.Notify(
                $"{status.Name} {status.TargetVersion} is available",
                $"Installed: {status.InstalledVersion}. Open Tools > Runtime Dependencies to update.");
            settings.Set(key, status.TargetVersion);
            changed = true;
        }

        if (changed)
        {
            settings.Write();
        }
    }
}
