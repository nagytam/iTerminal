namespace iConsole.Tasks;

public interface IBackgroundTask
{
    string Name { get; }
    string DisplayName { get; }
    TaskPriority Priority { get; }
    CancellationTokenSource CancellationTokenSource { get; }

    /// <summary>Where the task is paused and resumed; the task checks it at its checkpoints.</summary>
    PauseGate PauseGate { get; }
    Task RunAsync();
}