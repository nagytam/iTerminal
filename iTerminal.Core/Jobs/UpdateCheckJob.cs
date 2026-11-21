using iTerminal.Core.Services.BackgroundStart;
using iConsole.Localization;
using iConsole.Jobs;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Looks for a newer iTerminal release shortly after start-up, and every <see cref="Interval"/> after that, and offers
/// it in the update dialog where the copy can install it (the MSI, or a portable copy). A release is offered once per
/// run, so a periodic check opens the dialog only for a release it has not announced yet. Where nothing can be
/// installed (a Linux package), the release is only a notification.
/// </summary>
/// <remarks>
/// The dialog is resolved on demand: it is shown long after start-up, from this job's thread, and taking it as a
/// constructor parameter would pull the dialog into the job graph that is built while the desktop is created.
/// </remarks>
public class UpdateCheckJob(
    IUpdateService updates,
    INotificationService notifications,
    IServiceProvider serviceProvider,
    IBackgroundStartService backgroundStart) : Job
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    private string? _announced;
    private readonly object _gate = new();
    private TaskCompletionSource _wake = NewWake();
    private bool _manualRequested;

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Checks for a release now, as Help > Check for Updates asks. Returns at once: the check runs on this job's thread,
    /// and the result is shown from there (the dialog, or a toast saying there is nothing newer).
    /// </summary>
    public void CheckNow()
    {
        lock (_gate)
        {
            _manualRequested = true;
            _wake.TrySetResult();
        }
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        // The check runs when About is opened, or as configured (BackgroundStart.UpdateCheck).
        await backgroundStart.WaitForStartAsync(BackgroundServices.UpdateCheck, cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            bool manual;
            lock (_gate)
            {
                manual = _manualRequested;
                _manualRequested = false;
            }

            await updates.CheckAsync(cancellationToken);
            await OfferAsync(openDialog: true, manual, cancellationToken);

            Task wait;
            lock (_gate)
            {
                // A request that arrived during the check is honoured at once, not after the interval.
                wait = _manualRequested ? Task.CompletedTask : WaitForNextCheck(cancellationToken);
            }

            await wait;
        }
    }

    /// <summary>Waits for the interval, or for <see cref="CheckNow"/>, whichever comes first. Called under <see cref="_gate"/>.</summary>
    private Task WaitForNextCheck(CancellationToken cancellationToken)
    {
        _wake = NewWake();
        var wake = _wake.Task;
        return Task.WhenAny(Task.Delay(Interval, cancellationToken), wake);
    }

    internal Task OfferAsync(bool openDialog, CancellationToken cancellationToken) =>
        OfferAsync(openDialog, manual: false, cancellationToken);

    private async Task OfferAsync(bool openDialog, bool manual, CancellationToken cancellationToken)
    {
        UpdateInfo? update = updates.Available;
        if (update is null || updates.IsSkipped)
        {
            if (manual)
            {
                notifications.Notify(UiText.Get("Notify.UpToDate", "iTerminal is up to date"), string.Format(UiText.Get("Notify.UpToDateDetail", "No release newer than {0} was found."), updates.CurrentVersion));
            }

            return;
        }

        // A release already announced is not offered again by the scheduled checks, but a request the person made is answered.
        if (!manual && update.Version == _announced)
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

        string detail = updates.InstallKind == UpdateInstallKind.PackageManager
            ? UiText.Get("Update.WinGetManaged", "Installed with WinGet, which updates it. Run: winget upgrade TamasNagy.iTerminal")
            : string.Format(UiText.Get("Notify.UpdateDetail", "You have {0}. Download it from {1}"), updates.CurrentVersion, updates.ReleasesAddress);
        notifications.Notify(string.Format(UiText.Get("Notify.UpdateAvailable", "iTerminal {0} is available"), update.Version), detail);
    }
}
