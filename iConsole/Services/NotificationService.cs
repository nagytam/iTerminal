namespace iConsole.Services;

public class NotificationService : INotificationService
{
    private sealed class Entry(Notification notification)
    {
        public Notification Notification { get; } = notification;
        public TimeSpan Remaining { get; set; } = notification.Duration;
    }

    private readonly Lock _lock = new();
    private readonly List<Entry> _active = [];
    private readonly Queue<Entry> _waiting = new();
    private readonly List<Notification> _history = [];
    private long _nextId;
    private long _version;

    public int MaxVisible { get; init; } = 4;

    public int MaxHistory { get; init; } = 100;

    public long Version => Interlocked.Read(ref _version);

    public event EventHandler? Changed;

    public IReadOnlyList<Notification> Active
    {
        get
        {
            lock (_lock)
            {
                return [.. _active.Select(entry => entry.Notification)];
            }
        }
    }

    public IReadOnlyList<Notification> History
    {
        get
        {
            lock (_lock)
            {
                return [.. _history];
            }
        }
    }

    /// <summary>
    /// How long a notification stays up when the caller does not say. An error stays longest: it is
    /// the one that has to be read, and usually the longest to read.
    /// </summary>
    public static TimeSpan DefaultDuration(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Error => TimeSpan.FromSeconds(10),
        NotificationSeverity.Warning => TimeSpan.FromSeconds(6),
        _ => TimeSpan.FromSeconds(4),
    };

    public Notification Notify(
        string title,
        string? message = null,
        NotificationSeverity severity = NotificationSeverity.Info,
        TimeSpan? duration = null)
    {
        title ??= string.Empty;
        message = string.IsNullOrWhiteSpace(message) ? null : message;

        Notification notification;
        var activeChanged = false;
        lock (_lock)
        {
            var repeated = _active.FirstOrDefault(entry =>
                entry.Notification.Title == title
                && entry.Notification.Message == message
                && entry.Notification.Severity == severity);
            if (repeated is not null)
            {
                repeated.Remaining = repeated.Notification.Duration;
                return repeated.Notification;
            }

            notification = new Notification(
                ++_nextId,
                title,
                message,
                severity,
                DateTime.Now,
                duration ?? DefaultDuration(severity));

            _history.Add(notification);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(0, _history.Count - MaxHistory);
            }

            _waiting.Enqueue(new Entry(notification));
            activeChanged = ShowWaiting();
        }

        if (activeChanged)
        {
            OnChanged();
        }

        return notification;
    }

    public bool Dismiss(long id)
    {
        bool found;
        var activeChanged = false;
        lock (_lock)
        {
            found = _active.RemoveAll(entry => entry.Notification.Id == id) > 0;
            if (found)
            {
                ShowWaiting();
                activeChanged = true;
            }
            else if (_waiting.Any(entry => entry.Notification.Id == id))
            {
                var kept = _waiting.Where(entry => entry.Notification.Id != id).ToList();
                _waiting.Clear();
                kept.ForEach(_waiting.Enqueue);
                found = true;
            }
        }

        if (activeChanged)
        {
            OnChanged();
        }

        return found;
    }

    public void DismissAll()
    {
        bool activeChanged;
        lock (_lock)
        {
            activeChanged = _active.Count > 0;
            _active.Clear();
            _waiting.Clear();
        }

        if (activeChanged)
        {
            OnChanged();
        }
    }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        var activeChanged = false;
        lock (_lock)
        {
            foreach (var entry in _active)
            {
                if (entry.Notification.Duration != Timeout.InfiniteTimeSpan)
                {
                    entry.Remaining -= elapsed;
                }
            }

            activeChanged = _active.RemoveAll(entry =>
                entry.Notification.Duration != Timeout.InfiniteTimeSpan && entry.Remaining <= TimeSpan.Zero) > 0;

            // After the countdown, so a notification shown by this pass keeps its whole duration.
            activeChanged |= ShowWaiting();
        }

        if (activeChanged)
        {
            OnChanged();
        }
    }

    /// <summary>Moves waiting notifications on screen while there is room. Call under the lock.</summary>
    private bool ShowWaiting()
    {
        var shown = false;
        while (_active.Count < MaxVisible && _waiting.TryDequeue(out var entry))
        {
            _active.Add(entry);
            shown = true;
        }
        return shown;
    }

    private void OnChanged()
    {
        Interlocked.Increment(ref _version);
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A subscriber that throws must not fail whatever was posting: that is a file copy
            // finishing, or a key handler.
        }
    }
}
