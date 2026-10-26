namespace iConsole.Services;

/// <summary>The real clocks: <see cref="DateTime"/> for the wall clock, <see cref="Environment.TickCount64"/> for elapsed time.</summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    public DateTime Now => DateTime.Now;

    public DateTime UtcNow => DateTime.UtcNow;

    public long MonotonicMilliseconds => Environment.TickCount64;
}
