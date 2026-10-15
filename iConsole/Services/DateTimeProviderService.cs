namespace iConsole.Services;

public class DateTimeProviderService : IDateTimeProviderService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);
    private DateTime _cachedNow;
    private DateTime _cachedUtcNow;
    private DateTime _lastUpdated;

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
        if ((DateTime.Now - _lastUpdated) >= RefreshInterval)
        {
            _cachedNow = DateTime.Now;
            _cachedUtcNow = DateTime.UtcNow;
            _lastUpdated = DateTime.Now;
        }
    }
}
