using System.Diagnostics;
using iConsole.Jobs;
using iConsole.Services;
using iTerminal.Core.Services.ToDo;

namespace iTerminal.Core.Jobs;

/// <summary>
/// Checks the task list for reminders that have come due. The check runs every few seconds on its own thread, so a
/// reminder is given whether or not the task list is open. A reminder that fell due while the app was closed is
/// given on the first check after start-up.
/// </summary>
public class ToDoReminderJob(ToDoReminderService reminders, IDateTimeProviderService dateTimeProviderService) : Job
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
                reminders.GiveDueReminders(dateTimeProviderService.Now);
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"{nameof(ToDoReminderJob)} failed: {exception}");
            }
        }

        return Task.CompletedTask;
    }
}
