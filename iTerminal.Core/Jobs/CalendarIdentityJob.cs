using iConsole.Jobs;
using iTerminal.Core.Model.Calendar;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Writes the identities given to calendar meetings when the list was loaded, so they are the same after a restart.
/// The write is a settings save, which must not run on the render thread, so it happens here, on the job's own thread.
/// A failed write is logged and tried again on the next check, so the identities are not lost to one failure.
/// </summary>
public class CalendarIdentityJob(MeetingStore meetings, Serilog.ILogger logger) : Job
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    protected override Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Returns as soon as the token fires, so shutdown is not held up by the interval.
            cancellationToken.WaitHandle.WaitOne(TickInterval);
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (meetings.HasPendingIdentities)
                {
                    meetings.PersistPendingIdentities();
                }
            }
            catch (Exception exception)
            {
                logger.Warning(exception, "Could not save the calendar's meeting identities; trying again");
            }
        }

        return Task.CompletedTask;
    }
}
