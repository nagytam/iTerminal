using System.Collections.Concurrent;
using System.Diagnostics;
using iConsole.Jobs;
using iConsole.Services;

namespace iConsole.Diagnostics;

/// <summary>What the watchdog observed about one stalled pass of the main loop.</summary>
/// <param name="Label">What was running: <c>key</c>, <c>mouse</c> or <c>render</c>.</param>
/// <param name="ElapsedMilliseconds">How long it had run when reported, or in total once it finished.</param>
public sealed record StallEventArgs(string Label, long ElapsedMilliseconds);

/// <summary>
/// Times the passes of the main loop that must return within the budget (key handlers and render passes, see CLAUDE.md)
/// and reports any that run past it.
///
/// <para>The loop is measured, not interrupted: a handler that never returns cannot be stopped from here. The watchdog
/// is a job of its own thread, so it reports the stall while it is still happening, once per pass, and reports the total
/// duration when the pass finally ends. <see cref="Stalled"/> and <see cref="Recovered"/> are raised on the watchdog's
/// thread; a handler of either must not touch the control tree (post a notification instead).</para>
/// </summary>
public sealed class MainLoopWatchDog : Job
{
    /// <summary>How often the watchdog looks at the passes in progress.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly ITimeProvider _timeProvider;
    private readonly ConcurrentDictionary<long, Pass> _active = new();
    private long _nextId;
    private long _stallCount;

    public MainLoopWatchDog(ITimeProvider timeProvider) : this(timeProvider, TimeSpan.FromMilliseconds(200))
    {
    }

    public MainLoopWatchDog(ITimeProvider timeProvider, TimeSpan threshold)
    {
        _timeProvider = timeProvider;
        Threshold = threshold;
    }

    /// <summary>The longest a pass may run before it counts as a stall. The default is the 200 ms budget for handlers.</summary>
    public TimeSpan Threshold { get; }

    /// <summary>How many passes have been reported as stalled since the watchdog started.</summary>
    public long StallCount => Interlocked.Read(ref _stallCount);

    /// <summary>Raised once for each pass that runs past <see cref="Threshold"/>, while it is still running.</summary>
    public event EventHandler<StallEventArgs>? Stalled;

    /// <summary>Raised when a stalled pass finishes, with how long it took altogether.</summary>
    public event EventHandler<StallEventArgs>? Recovered;

    /// <summary>
    /// Starts timing a pass. Dispose the returned scope when the pass ends; a pass that is not disposed is reported as stalled
    /// for as long as it stays open.
    /// </summary>
    public IDisposable Watch(string label)
    {
        var id = Interlocked.Increment(ref _nextId);
        var pass = new Pass(label, _timeProvider.MonotonicMilliseconds);
        _active[id] = pass;
        return new Scope(this, id, pass);
    }

    /// <summary>
    /// Looks at every pass in progress and reports the ones that have run past the threshold. The job calls this on each poll;
    /// a test calls it directly after moving the clock.
    /// </summary>
    public void Check()
    {
        var now = _timeProvider.MonotonicMilliseconds;
        foreach (var pass in _active.Values)
        {
            var elapsed = now - pass.StartMilliseconds;
            if (elapsed < Threshold.TotalMilliseconds || Interlocked.CompareExchange(ref pass.IsReported, 1, 0) != 0)
            {
                continue;
            }

            Interlocked.Increment(ref _stallCount);
            Debug.WriteLine($"Main loop stalled: {pass.Label} has run for {elapsed} ms (budget {Threshold.TotalMilliseconds} ms).");
            Stalled?.Invoke(this, new StallEventArgs(pass.Label, elapsed));
        }
    }

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Check();
            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Finish(long id, Pass pass)
    {
        _active.TryRemove(id, out _);
        if (Interlocked.CompareExchange(ref pass.IsReported, 0, 0) == 0)
        {
            return;
        }

        var elapsed = _timeProvider.MonotonicMilliseconds - pass.StartMilliseconds;
        Debug.WriteLine($"Main loop recovered: {pass.Label} took {elapsed} ms in total.");
        Recovered?.Invoke(this, new StallEventArgs(pass.Label, elapsed));
    }

    private sealed class Pass(string label, long startMilliseconds)
    {
        public string Label { get; } = label;
        public long StartMilliseconds { get; } = startMilliseconds;

        /// <summary>1 once the pass has been reported as stalled; 0 until then.</summary>
        public int IsReported;
    }

    private sealed class Scope(MainLoopWatchDog watchdog, long id, Pass pass) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                watchdog.Finish(id, pass);
            }
        }
    }
}
