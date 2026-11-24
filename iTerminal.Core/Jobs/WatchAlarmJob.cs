using System.Globalization;
using iConsole.Jobs;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model.Watch;
using iTerminal.Core.Services.Watch;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Rings the alarms and announces the finished timers, once a second, whether or not the Watch dialog is open. An alarm
/// is a notification that stays until it is dismissed; a timer's is an ordinary one. The check is per minute for alarms
/// (<see cref="WatchAlarm.IsDue"/>), so an alarm rings once a day even though the job looks every second.
///
/// <para>Time stands still while the terminal is lent to a child process (Ctrl+O), the same as the notification clock, so a
/// timer that runs out behind the shell is announced when the terminal comes back rather than lost.</para>
/// </summary>
public class WatchAlarmJob(
    WatchStore store,
    WatchTimers timers,
    IDateTimeProviderService dateTimeProviderService,
    INotificationService notificationService,
    Serilog.ILogger logger,
    ConsoleOwnership? consoleOwnership = null) : Job
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Returns as soon as the token fires, so shutdown is not held up by the interval.
            cancellationToken.WaitHandle.WaitOne(TickInterval);
            if (cancellationToken.IsCancellationRequested || consoleOwnership?.IsLent == true)
            {
                continue;
            }

            try
            {
                Check(dateTimeProviderService.Now);
            }
            catch (Exception exception)
            {
                logger.Warning(exception, "The watch could not check its alarms and timers; trying again");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>One pass: rings the due alarms and announces the finished timers. Public so a test can run a pass at a chosen time.</summary>
    public void Check(DateTime now)
    {
        foreach (var alarm in store.Alarms)
        {
            if (!WatchAlarm.IsDue(alarm, now))
            {
                continue;
            }

            notificationService.Notify(alarm.DisplayLabel, alarm.Time.ToString("HH:mm", CultureInfo.InvariantCulture), NotificationSeverity.Warning, Timeout.InfiniteTimeSpan);

            // The store records the day in memory before it saves, so a failed save (which throws here and is logged) cannot ring the alarm again this minute.
            store.MarkRung(alarm.Uid, DateOnly.FromDateTime(now));
        }

        foreach (var label in timers.TakeFinished(now).Select(timer => timer.Label))
        {
            notificationService.Notify(label.Length == 0 ? DefaultTimerLabel() : label, UiText.Get("Watch.TimerFinished", "Time is up"));
        }
    }

    private static string DefaultTimerLabel() => UiText.Get("Watch.DefaultTimerLabel", "Timer");
}
