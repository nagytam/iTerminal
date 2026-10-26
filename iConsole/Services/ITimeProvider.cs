namespace iConsole.Services;

/// <summary>
/// Where the application reads the time from. Production code uses <see cref="SystemTimeProvider"/>;
/// tests use <see cref="VirtualTimeProvider"/> and move the clock forward instead of sleeping.
/// </summary>
public interface ITimeProvider
{
    /// <summary>The local wall-clock time.</summary>
    DateTime Now { get; }

    /// <summary>The UTC wall-clock time.</summary>
    DateTime UtcNow { get; }

    /// <summary>
    /// Milliseconds on a clock that never goes backwards, for measuring how long something has taken.
    /// The wall clock can be set back or stepped by daylight saving, so it must not be used for that.
    /// </summary>
    long MonotonicMilliseconds { get; }
}
