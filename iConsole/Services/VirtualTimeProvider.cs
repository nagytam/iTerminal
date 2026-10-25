namespace iConsole.Services;

/// <summary>
/// A clock that only moves when a test says so. Time does not pass on its own, so a test that advances it by
/// the length of a delay sees the delay has ended without waiting for it.
/// </summary>
public sealed class VirtualTimeProvider : ITimeProvider
{
    private readonly Lock _gate = new();
    private DateTime _utcNow;
    private long _monotonicMilliseconds;

    /// <summary>Starts at 2026-01-01 00:00 UTC, with the monotonic clock at zero.</summary>
    public VirtualTimeProvider() : this(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
    {
    }

    public VirtualTimeProvider(DateTime startUtc)
    {
        _utcNow = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
    }

    public DateTime Now
    {
        get
        {
            lock (_gate)
            {
                return _utcNow.ToLocalTime();
            }
        }
    }

    public DateTime UtcNow
    {
        get
        {
            lock (_gate)
            {
                return _utcNow;
            }
        }
    }

    public long MonotonicMilliseconds
    {
        get
        {
            lock (_gate)
            {
                return _monotonicMilliseconds;
            }
        }
    }

    /// <summary>Moves both clocks forward by <paramref name="duration"/>.</summary>
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Virtual time only moves forward.");
        }

        lock (_gate)
        {
            _utcNow += duration;
            _monotonicMilliseconds += (long)duration.TotalMilliseconds;
        }
    }
}
