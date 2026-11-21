namespace iTerminal.Core.Model.Watch;

/// <summary>
/// A stopwatch: starts and stops, records laps while it runs, and forgets everything on <see cref="Reset"/>.
/// It lives for the session only, so nothing here is persisted.
/// </summary>
public sealed class WatchStopwatch
{
    private readonly WatchRun _run = new();
    private readonly List<TimeSpan> _laps = [];

    public bool IsRunning => _run.IsRunning;

    /// <summary>The laps recorded so far, in the order they were taken; each is the time from the start of the stopwatch.</summary>
    public IReadOnlyList<TimeSpan> Laps => _laps;

    public TimeSpan Elapsed(DateTime now) => _run.Elapsed(now);

    public void Toggle(DateTime now) => _run.Toggle(now);

    /// <summary>Records a lap. A lap only means something while the stopwatch runs, so a stopped one ignores it.</summary>
    public bool Lap(DateTime now)
    {
        if (!_run.IsRunning)
        {
            return false;
        }

        _laps.Add(_run.Elapsed(now));
        return true;
    }

    /// <summary>Clears the time and the laps. A running stopwatch is stopped first, so a reset never leaves it ticking from zero.</summary>
    public void Reset()
    {
        _run.Reset();
        _laps.Clear();
    }

    /// <summary>
    /// <c>mm:ss.cc</c>, or <c>h:mm:ss.cc</c> from an hour up, with hundredths. Truncates rather than rounds, so the
    /// display never shows a time the stopwatch has not yet reached.
    /// </summary>
    public static string Format(TimeSpan elapsed)
    {
        var total = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
        long hundredths = total.Ticks / TimeSpan.TicksPerMillisecond / 10;
        long hours = hundredths / 360000;
        long minutes = hundredths / 6000 % 60;
        long seconds = hundredths / 100 % 60;
        long centiseconds = hundredths % 100;
        return hours > 0
            ? $"{hours}:{minutes:00}:{seconds:00}.{centiseconds:00}"
            : $"{minutes:00}:{seconds:00}.{centiseconds:00}";
    }
}
