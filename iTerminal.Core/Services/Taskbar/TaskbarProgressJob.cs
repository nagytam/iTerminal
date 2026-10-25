using iConsole.Jobs;
using iConsole.Tasks;

namespace iTerminal.Core.Services.Taskbar;

/// <summary>
/// Keeps the taskbar's progress indicator in step with the background tasks. It looks every quarter second and tells the
/// indicator only when what it should show has changed, so an idle application makes no calls at all.
/// </summary>
public sealed class TaskbarProgressJob(TaskManager taskManager, ITaskbarProgress taskbar) : Job
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private (TaskbarState State, long Percentage)? _shown;

    /// <summary>Brings the indicator in line with the tasks. Public so a test can do in one call what a poll does.</summary>
    public void Publish()
    {
        var now = TaskbarProgressCalculator.Compute(taskManager.Tasks.AsSnapshot());
        if (_shown is { } shown && shown == now)
        {
            return;
        }

        _shown = now;
        taskbar.Update(now.State, now.Percentage);
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    Publish();
                }
                catch (Exception exception)
                {
                    // A failing indicator must not stop the job: the next poll tries again.
                    System.Diagnostics.Debug.WriteLine($"Taskbar progress: {exception.Message}");
                }

                await Task.Delay(Interval, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            // Leave no indicator behind when the application stops.
            taskbar.Update(TaskbarState.None, 0);
        }
    }
}
