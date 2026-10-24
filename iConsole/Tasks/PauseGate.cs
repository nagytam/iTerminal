namespace iConsole.Tasks;

/// <summary>
/// A pause switch a task checks at its checkpoints. While it is paused, <see cref="WaitWhilePausedAsync"/> does not return
/// until it is resumed, or until the token is cancelled.
/// </summary>
public sealed class PauseGate
{
    private readonly Lock _lock = new();
    private TaskCompletionSource _resumed = CreateResumed();

    public bool IsPaused
    {
        get
        {
            lock (_lock)
            {
                return !_resumed.Task.IsCompleted;
            }
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_resumed.Task.IsCompleted)
            {
                _resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _resumed.TrySetResult();
        }
    }

    public Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        Task resumed;
        lock (_lock)
        {
            resumed = _resumed.Task;
        }

        return resumed.IsCompleted ? Task.CompletedTask : resumed.WaitAsync(cancellationToken);
    }

    private static TaskCompletionSource CreateResumed()
    {
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resumed.SetResult();
        return resumed;
    }
}
