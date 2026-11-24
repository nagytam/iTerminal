namespace iTerminal.Core.Model.Watch;

/// <summary>
/// Time that runs and stops: the elapsed part behind a stopwatch and a timer. It takes the time as a parameter
/// rather than reading a clock, so the dialog and the alarm job can share one clock and a test can drive it.
/// A clock that goes backwards counts as standing still, not as negative time.
/// </summary>
public sealed class WatchRun
{
    private TimeSpan _accumulated;
    private DateTime? _startedAt;

    public bool IsRunning => _startedAt is not null;

    public TimeSpan Elapsed(DateTime now)
    {
        if (_startedAt is not { } startedAt)
        {
            return _accumulated;
        }

        return _accumulated + TimeSpan.FromTicks(Math.Max(0, (now - startedAt).Ticks));
    }

    public void Start(DateTime now)
    {
        _startedAt ??= now;
    }

    public void Stop(DateTime now)
    {
        if (_startedAt is not null)
        {
            _accumulated = Elapsed(now);
            _startedAt = null;
        }
    }

    public void Toggle(DateTime now)
    {
        if (IsRunning)
        {
            Stop(now);
        }
        else
        {
            Start(now);
        }
    }

    public void Reset()
    {
        _accumulated = TimeSpan.Zero;
        _startedAt = null;
    }
}
