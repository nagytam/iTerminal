using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace iConsole.Tasks;

public abstract class BackgroundTask : IBackgroundTask, INotifyPropertyChanged
{
    public virtual string Name { get; protected set; } = string.Empty;
    public virtual string DisplayName => Name;
    public virtual TaskPriority Priority => TaskPriority.Background;
    public string Description { get; protected set; } = string.Empty;

    /// <summary>
    /// What the task is being done to, in a few words ("readme.txt to target"), or null when it has
    /// nothing shorter to say than its <see cref="Description"/>. The description is written for a
    /// progress window eighty columns wide, with whole paths in it; this is for anywhere narrower —
    /// a notification, mostly.
    /// </summary>
    public string? Subject { get; protected set; }

    private Data.TaskStatus _status = Data.TaskStatus.Started;
    public virtual Data.TaskStatus Status 
    {
        get 
        { 
            return _status; 
        }
        protected set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        } 
    }

    private string? _error;

    /// <summary>
    /// The failure message when the task finished without doing what it was asked to, otherwise
    /// null. Tasks here report failures by catching the exception and describing it rather than
    /// letting it escape <see cref="ExecuteAsync"/> — which leaves <see cref="Status"/> reading
    /// <see cref="Data.TaskStatus.Completed"/> for a run that copied nothing. UI that dismisses
    /// itself once the work is over needs to tell those two apart, so that it can stay on screen
    /// long enough for the user to read why.
    /// </summary>
    public string? Error
    {
        get
        {
            return _error;
        }
        protected set
        {
            if (_error != value)
            {
                _error = value;
                OnPropertyChanged(nameof(Error));
            }
        }
    }

    public CancellationTokenSource CancellationTokenSource { get; private set; }
    protected CancellationToken CancellationToken { get; private set; }
    protected IProgress<uint> Progress { get; private set; }
    private long _progressPercentage = 0;
    private readonly Lock _lock = new();
    public long ProgressPercentage 
    {
        get
        {
            lock (_lock)
            {
                return _progressPercentage;
            }
        }
        protected set 
        {
            if (_progressPercentage != value)
            {
                lock (_lock)
                {
                    _progressPercentage = value;
                    OnPropertyChanged(nameof(ProgressPercentage));
                }
            }
        } 
    }
    public Stopwatch Stopwatch { get; private set; } = new();
    public TimeSpan RemainingTime { get; private set; } = new();

    protected virtual void OnProgressChanged(long value)
    {
        try
        {
            ProgressPercentage = value;
            var elapsedTime = Stopwatch.Elapsed;
            var totalSeconds = elapsedTime.TotalSeconds;
            if (ProgressPercentage > 0 && ProgressPercentage <= 100)
            {
                var estimatedSeconds = totalSeconds * 100 / ProgressPercentage;
                if (!double.IsInfinity(estimatedSeconds))
                {
                    RemainingTime = TimeSpan.FromSeconds(estimatedSeconds) - elapsedTime;
                }
            }
        }
        catch
        {
            Debugger.Break();
        }
    }

    protected BackgroundTask()
    {
        Name = GetType().FullName ?? string.Empty;
        Progress = new Progress<uint>((value) => OnProgressChanged(value));
        CancellationTokenSource = new CancellationTokenSource();
        CancellationToken = CancellationTokenSource.Token;
    }

    public async Task RunAsync()
    {
        Status = Data.TaskStatus.Started;
        Stopwatch.Start();
        await ExecuteAsync();
        if (!CancellationToken.IsCancellationRequested)
        {
            Status = Data.TaskStatus.Completed;
        }
        else
        {
            Status = Data.TaskStatus.Canceled;
        }
        Stopwatch.Stop();
    }

    public abstract Task ExecuteAsync();

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    #endregion
}
