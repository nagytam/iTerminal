namespace iConsole.Services;

/// <summary>
/// Keeps the time of the last activity on the monotonic clock, so setting the wall clock back cannot make the screen look idle
/// for an hour. Written from the input thread and read from the render thread, hence the interlocked access.
/// </summary>
public sealed class UserActivityService : IUserActivityService
{
    private readonly ITimeProvider _timeProvider;
    private long _lastActivityMilliseconds;

    public UserActivityService(ITimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _lastActivityMilliseconds = _timeProvider.MonotonicMilliseconds;
    }

    public void NoteActivity() =>
        Interlocked.Exchange(ref _lastActivityMilliseconds, _timeProvider.MonotonicMilliseconds);

    public TimeSpan IdleFor
    {
        get
        {
            long last = Interlocked.Read(ref _lastActivityMilliseconds);
            long idle = Math.Max(0, _timeProvider.MonotonicMilliseconds - last);
            return TimeSpan.FromMilliseconds(idle);
        }
    }
}
