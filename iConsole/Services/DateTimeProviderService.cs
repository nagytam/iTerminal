namespace iConsole.Services;

public class DateTimeProviderService : IDateTimeProviderService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    private readonly ITimeProvider _timeProvider;
    private DateTime _cachedNow;
    private DateTime _cachedUtcNow;
    // The refresh is timed on the monotonic clock: a wall clock set back (or a daylight-saving step) made the elapsed time
    // negative, so the cached time was not refreshed until the wall clock had caught up again.
    private long _lastRefreshTick = long.MinValue / 2;
    private readonly Lock _gate = new();

    public DateTimeProviderService() : this(new SystemTimeProvider())
    {
    }

    public DateTimeProviderService(ITimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateTime Now
    {
        get
        {
            UpdateCacheIfNeeded();
            return _cachedNow;
        }
    }

    public DateTime UtcNow
    {
        get
        {
            UpdateCacheIfNeeded();
            return _cachedUtcNow;
        }
    }

    private void UpdateCacheIfNeeded()
    {
        if (_timeProvider.MonotonicMilliseconds - Volatile.Read(ref _lastRefreshTick) < RefreshInterval.TotalMilliseconds)
        {
            return;
        }

        lock (_gate)
        {
            if (_timeProvider.MonotonicMilliseconds - _lastRefreshTick < RefreshInterval.TotalMilliseconds)
            {
                return;
            }

            _cachedNow = _timeProvider.Now;
            _cachedUtcNow = _timeProvider.UtcNow;
            Volatile.Write(ref _lastRefreshTick, _timeProvider.MonotonicMilliseconds);
        }
    }
}
