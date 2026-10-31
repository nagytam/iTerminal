using iConsole.Localization;
using iConsole.Jobs;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Looks for a newer iTerminal release shortly after start-up, and offers it in the update dialog. Later checks
/// (every <see cref="Interval"/>) only post a notification, so a running session is not interrupted twice.
/// </summary>
/// <remarks>
/// The dialog is resolved on demand: it is shown long after start-up, from this job's thread, and taking it as a
/// constructor parameter would pull the dialog into the job graph that is built while the desktop is created.
/// </remarks>
public class UpdateCheckJob(
    IUpdateService updates,
    INotificationService notifications,
    IServiceProvider serviceProvider) : Job
{
    internal static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    private string? _announced;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(StartDelay, cancellationToken);
        bool first = true;
        while (!cancellationToken.IsCancellationRequested)
        {
            await updates.CheckAsync(cancellationToken);
            await OfferAsync(first, cancellationToken);
            first = false;
            await Task.Delay(Interval, cancellationToken);
        }
    }

    internal async Task OfferAsync(bool openDialog, CancellationToken cancellationToken)
    {
        UpdateInfo? update = updates.Available;
        if (update is null || updates.IsSkipped || update.Version == _announced)
        {
            return;
        }

        _announced = update.Version;
        // Only where the app can install the release itself is there anything to ask; elsewhere a toast says it is out.
        if (openDialog && updates.CanInstall && Desktop?.Screens.FirstOrDefault() is { } screen)
        {
            var dialog = serviceProvider.GetRequiredService<UpdateDialog>();
            dialog.OnInstallerStarted = () => Desktop?.CancellationTokenSource.Cancel();
            await screen.ShowDialogAsync(dialog);
            return;
        }

        notifications.Notify(string.Format(UiText.Get("Notify.UpdateAvailable", "iTerminal {0} is available"), update.Version), string.Format(UiText.Get("Notify.UpdateDetail", "You have {0}. Download it from {1}"), updates.CurrentVersion, updates.ReleasesAddress));
    }
}
