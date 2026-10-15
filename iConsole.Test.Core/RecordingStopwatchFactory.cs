using iConsole.Services;

namespace iConsole.Test.Core;

/// <summary>
/// IStopwatchFactory that captures each stopwatch so tests can inspect elapsed times.
/// </summary>
public sealed class RecordingStopwatchFactory : IStopwatchFactory
{
    private readonly List<RecordingStopwatch> _created = [];

    public IReadOnlyList<RecordingStopwatch> Created => _created;

    public IStopwatch StartNew()
    {
        var sw = new RecordingStopwatch();
        lock (_created) { _created.Add(sw); }
        sw.Start();
        return sw;
    }
}

public sealed class RecordingStopwatch : IStopwatch
{
    private readonly System.Diagnostics.Stopwatch _inner = new();

    public void Start() => _inner.Start();
    public void Stop() => _inner.Stop();
    public void Restart() => _inner.Restart();
    public TimeSpan Elapsed => _inner.Elapsed;
    public long ElapsedMilliseconds => _inner.ElapsedMilliseconds;
    public bool IsRunning => _inner.IsRunning;
}
