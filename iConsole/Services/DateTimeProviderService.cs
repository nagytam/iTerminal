namespace iConsole.Services;

public class DateTimeProviderService : IDateTimeProviderService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    private DateTime _cachedNow;
    private DateTime _cachedUtcNow;
    // The refresh is timed on the monotonic clock: a wall clock set back (or a daylight-saving step) made the elapsed time
    // negative, so the cached time was not refreshed until the wall clock had caught up again.
    private long _lastRefreshTick = long.MinValue / 2;
    private readonly Lock _gate = new();

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
        if (Environment.TickCount64 - Volatile.Read(ref _lastRefreshTick) < RefreshInterval.TotalMilliseconds)
        {
            return;
        }

        lock (_gate)
        {
            if (Environment.TickCount64 - _lastRefreshTick < RefreshInterval.TotalMilliseconds)
            {
                return;
            }

            _cachedNow = DateTime.Now;
            _cachedUtcNow = DateTime.UtcNow;
            Volatile.Write(ref _lastRefreshTick, Environment.TickCount64);
        }
    }
}
