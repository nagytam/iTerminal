using iConsole.Data;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace iConsole.Tasks;

public class TaskManager : INotifyPropertyChanged, IDisposable
{
    private readonly Lock _queueLock = new();
    private readonly PriorityQueue<IBackgroundTask, (int Priority, long Sequence)> _pendingTasks = new();
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

    public Task Add(IBackgroundTask task)
    {
        Tasks.Add(task);
        var priority = ((int)task.Priority, Interlocked.Increment(ref _sequence));
        lock (_queueLock)
        {
            _pendingTasks.Enqueue(task, priority);
        }

        OnPropertyChanged(nameof(Tasks));
        _pendingSignal.Release();
        return Task.CompletedTask;
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
                if (_pendingTasks.TryDequeue(out var nextTask, out _))
                {
                    task = nextTask;
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