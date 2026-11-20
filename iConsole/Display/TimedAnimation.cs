namespace iConsole.Display;

/// <summary>
/// A short animation measured on a monotonic millisecond clock (<see cref="iConsole.Services.ITimeProvider.MonotonicMilliseconds"/>).
/// It runs for <see cref="Duration"/> milliseconds from <see cref="Start"/>, and <see cref="Step"/> divides that time into
/// <see cref="StepCount"/> equal parts, counted from 0 (the step is at most <see cref="StepCount"/> - 1 while it runs).
/// Start and Stop may be called from the input thread while Step is read on the render loop, so the state is kept behind a lock.
/// A running flag, not a time stamp, says whether the animation runs, because a clock can legitimately read 0.
/// </summary>
public sealed class TimedAnimation
{
    private readonly Lock _gate = new();
    private long _startedAt;
    private bool _isRunning;

    public TimedAnimation(long durationMilliseconds, int stepCount)
    {
        if (durationMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(durationMilliseconds), "The animation needs a positive duration.");
        }
        if (stepCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stepCount), "The animation needs at least one step.");
        }
        Duration = durationMilliseconds;
        StepCount = stepCount;
    }

    /// <summary>How long the animation runs, in milliseconds.</summary>
    public long Duration { get; }

    /// <summary>How many steps the animation is divided into.</summary>
    public int StepCount { get; }

    /// <summary>
    /// True from <see cref="Start"/> until <see cref="Stop"/> is called, or until a <see cref="Step"/> call finds that
    /// <see cref="Duration"/> has passed. Time is not read here, so a caller that needs the current state reads <see cref="Step"/>.
    /// </summary>
    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _isRunning;
            }
        }
    }

    /// <summary>Begins the animation at <paramref name="nowMilliseconds"/>, replacing a run already in progress.</summary>
    public void Start(long nowMilliseconds)
    {
        lock (_gate)
        {
            _startedAt = nowMilliseconds;
            _isRunning = true;
        }
    }

    /// <summary>Ends the animation at once.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _isRunning = false;
        }
    }

    /// <summary>
    /// The current step at <paramref name="nowMilliseconds"/>: 0 for the first part of the run, rising to
    /// <see cref="StepCount"/> - 1. Once the run has passed its duration, or was never started or was stopped, the step
    /// is <see cref="StepCount"/> ("done"), so a caller can tell the end from the first step with one read. A run that
    /// has passed its duration stops itself.
    /// </summary>
    public int Step(long nowMilliseconds)
    {
        lock (_gate)
        {
            if (!_isRunning)
            {
                return StepCount;
            }
            var elapsed = nowMilliseconds - _startedAt;
            if (elapsed >= Duration)
            {
                _isRunning = false;
                return StepCount;
            }
            // A clock that reads before the start is treated as the start of the run.
            return elapsed <= 0 ? 0 : (int)Math.Min(StepCount - 1, elapsed * StepCount / Duration);
        }
    }
}
