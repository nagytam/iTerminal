using iConsole.Data;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace iConsole.Tasks;

public class TaskManager : INotifyPropertyChanged, IDisposable
{
    private readonly Lock _queueLock = new();
    /// <summary>The tasks waiting for a worker. A pending task is picked by priority, then by its place in the queue.</summary>
    private readonly List<PendingTask> _pendingTasks = [];
    private readonly SemaphoreSlim _pendingSignal = new(0);
    private readonly CancellationTokenSource _schedulerCancellationTokenSource = new();
    private readonly Task[] _workers;
    private long _sequence;

    /// <summary>How many tasks can run at once.</summary>
    public int WorkerCount => _workers.Length;

    public ConcurrentObservableCollection<IBackgroundTask> Tasks { get; private set; } = [];

    // Default to several workers so a high-priority visible refresh can run concurrently with
    // in-flight background prefetch tasks instead of waiting behind them on a single thread.
    private static readonly int DefaultWorkerCount = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    public TaskManager()
        : this(DefaultWorkerCount)
    {
    }

    // No default for workerCount: with one, the container saw a constructor it could satisfy that
    // takes more parameters than the parameterless one, picked it, and ran the whole app on a single
    // worker — a folder copy's discovery task then held that worker while the copy that drains it
    // never started.
    public TaskManager(int workerCount)
    {
        workerCount = Math.Max(1, workerCount);
        _workers = new Task[workerCount];
        for (var index = 0; index < workerCount; index++)
        {
            _workers[index] = Task.Run(ProcessQueueAsync);
        }
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion

    private sealed class PendingTask(IBackgroundTask item, int priority, long sequence)
    {
        public IBackgroundTask Item { get; } = item;

        public int Priority { get; } = priority;

        /// <summary>Where the task is in the queue within its priority: lower runs first. Moving a task swaps these.</summary>
        public long Sequence { get; set; } = sequence;
    }

    public Task Add(IBackgroundTask task)
    {
        Tasks.Add(task);
        lock (_queueLock)
        {
            _pendingTasks.Add(new PendingTask(task, (int)task.Priority, Interlocked.Increment(ref _sequence)));
        }

        OnPropertyChanged(nameof(Tasks));
        _pendingSignal.Release();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pauses or resumes a task. A queued task that is paused is not started until it is resumed; a running one stops at its
    /// next checkpoint (a copy at its next chunk, and any other task only once it has finished its step).
    /// </summary>
    public void SetPaused(IBackgroundTask task, bool paused)
    {
        if (paused)
        {
            task.PauseGate.Pause();
            OnPropertyChanged(nameof(Tasks));
            return;
        }

        task.PauseGate.Resume();
        bool queued;
        lock (_queueLock)
        {
            queued = _pendingTasks.Any(pending => ReferenceEquals(pending.Item, task));
        }

        if (queued)
        {
            // A worker may be waiting on the queue with nothing to take while this task was paused.
            _pendingSignal.Release();
        }

        OnPropertyChanged(nameof(Tasks));
    }

    /// <summary>
    /// Moves a queued task one place earlier (or later) among the queued tasks of its priority. A task that has started is
    /// not in the queue, so it is not moved.
    /// </summary>
    public void MoveInQueue(IBackgroundTask task, bool earlier)
    {
        lock (_queueLock)
        {
            var ordered = _pendingTasks.OrderBy(pending => pending.Priority).ThenBy(pending => pending.Sequence).ToList();
            var index = ordered.FindIndex(pending => ReferenceEquals(pending.Item, task));
            var other = index + (earlier ? -1 : 1);
            if (index < 0 || other < 0 || other >= ordered.Count || ordered[other].Priority != ordered[index].Priority)
            {
                return;
            }

            (ordered[index].Sequence, ordered[other].Sequence) = (ordered[other].Sequence, ordered[index].Sequence);
        }

        OnPropertyChanged(nameof(Tasks));
    }

    public void CancelTask(Type type)
    {
       foreach (var task in Tasks.AsSnapshot()
            .Where(t => t.GetType().FullName == type.FullName))
       {
           task.CancellationTokenSource.Cancel();
       }
    }

    public IEnumerable<IBackgroundTask?> GetTasks<T>(Func<T, bool> compare) where T : class
    {
        return Tasks.AsSnapshot().Where(t => t is T typed && compare(typed));
    }

    private async Task ProcessQueueAsync()
    {
        while (!_schedulerCancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                await _pendingSignal.WaitAsync(_schedulerCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            IBackgroundTask? task = null;
            lock (_queueLock)
            {
                // A paused task waits in the queue: the worker takes the next one that may start.
                var next = _pendingTasks
                    .Where(pending => !pending.Item.PauseGate.IsPaused)
                    .MinBy(pending => (pending.Priority, pending.Sequence));
                if (next is not null)
                {
                    _pendingTasks.Remove(next);
                    task = next.Item;
                }
            }

            if (task is null)
            {
                continue;
            }

            try
            {
                if (!task.CancellationTokenSource.IsCancellationRequested)
                {
                    Debug.WriteLine($"Run task {task.Name}");
                    await task.RunAsync();
                }
            }
            catch
            {
            }
            finally
            {
                Tasks.Remove(task);
                OnPropertyChanged(nameof(Tasks));
            }
        }
    }

    public void Dispose()
    {
        if (_schedulerCancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _schedulerCancellationTokenSource.Cancel();
        for (var index = 0; index < _workers.Length; index++)
        {
            _pendingSignal.Release();
        }

        try
        {
            Task.WaitAll(_workers, TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _pendingSignal.Dispose();
        _schedulerCancellationTokenSource.Dispose();
    }
}