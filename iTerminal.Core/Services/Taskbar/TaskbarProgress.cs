using iConsole.Data;
using iConsole.Tasks;

namespace iTerminal.Core.Services.Taskbar;

/// <summary>What the taskbar's progress indicator shows for the application's window.</summary>
public enum TaskbarState
{
    /// <summary>Nothing is running: no indicator.</summary>
    None,

    /// <summary>Work is running but its progress is not known yet: a moving indicator.</summary>
    Indeterminate,

    /// <summary>Work is running and <see cref="ITaskbarProgress.Update"/> gives how far it has got.</summary>
    Normal,

    /// <summary>Everything running has been paused: the indicator shows the progress, paused.</summary>
    Paused,
}

/// <summary>
/// The taskbar's progress indicator for this application's window. The Windows implementation drives the taskbar button;
/// everywhere else it does nothing (<see cref="NullTaskbarProgress"/>).
/// </summary>
public interface ITaskbarProgress
{
    /// <summary>Shows <paramref name="state"/>; <paramref name="percentage"/> (0 to 100) is used by the normal and paused states.</summary>
    void Update(TaskbarState state, long percentage);
}

/// <summary>The indicator on a system without one, or where it is not wanted.</summary>
public sealed class NullTaskbarProgress : ITaskbarProgress
{
    public static readonly NullTaskbarProgress Instance = new();

    public void Update(TaskbarState state, long percentage)
    {
    }
}

/// <summary>
/// Works out what the indicator should show from the background tasks: the average progress of the tasks that are running,
/// shown as paused when every running task is paused. A task that has finished, or has not started, does not count.
/// </summary>
public static class TaskbarProgressCalculator
{
    public static (TaskbarState State, long Percentage) Compute(IEnumerable<IBackgroundTask> tasks)
    {
        var running = tasks
            .OfType<BackgroundTask>()
            .Where(task => task.Status == iConsole.Data.TaskStatus.Started)
            .ToList();

        if (running.Count == 0)
        {
            return (TaskbarState.None, 0);
        }

        var average = (long)running.Average(task => task.ProgressPercentage);
        var percentage = Math.Clamp(average, 0, 100);
        if (running.All(task => task.IsPaused))
        {
            return (TaskbarState.Paused, percentage);
        }

        // Nothing has reported progress yet: a moving indicator says "working" without claiming a percentage.
        return percentage == 0
            ? (TaskbarState.Indeterminate, 0)
            : (TaskbarState.Normal, percentage);
    }
}
