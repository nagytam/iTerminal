using iConsole.Services;
using System.Diagnostics;

namespace iConsole.Jobs;

/// <summary>
/// The clock behind <see cref="INotificationService"/>: every <see cref="TickInterval"/> it hands the
/// service the time that passed, which is what expires a toast and lets a waiting one take its place.
///
/// <para>It runs on a job of its own rather than from <see cref="Controls.Control.OnIdle"/>, which is
/// what the first toast counted in: idle passes come as often as the renderer has nothing to do — a
/// thousand a second on a quiet screen, none at all while a dialog repaints — so a count of them is
/// not a length of time.</para>
///
/// <para>Time stands still while the terminal is lent to a child process (Ctrl+O): nothing is drawn
/// then, and a "copy finished" that expired behind the shell would never have been seen.</para>
/// </summary>
public class NotificationJob(INotificationService notificationService, ConsoleOwnership? consoleOwnership = null) : Job
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        var lastTick = Stopwatch.GetTimestamp();
        while (!cancellationToken.IsCancellationRequested)
        {
            // Returns as soon as the token fires, so shutdown is not held up by the interval.
            cancellationToken.WaitHandle.WaitOne(TickInterval);

            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(lastTick, now);
            lastTick = now;

            if (consoleOwnership?.IsLent == true)
            {
                continue;
            }

            try
            {
                notificationService.Advance(elapsed);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Notification tick failed: {exception}");
            }
        }
        return Task.CompletedTask;
    }
}
