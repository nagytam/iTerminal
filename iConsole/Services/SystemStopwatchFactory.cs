using System.Diagnostics;

namespace iConsole.Services;

public sealed class SystemStopwatchFactory : IStopwatchFactory
{
    public static readonly SystemStopwatchFactory Instance = new();

    public IStopwatch StartNew() => new SystemStopwatch(Stopwatch.StartNew());

    private sealed class SystemStopwatch(Stopwatch sw) : IStopwatch
    {
        public void Start() => sw.Start();
        public void Stop() => sw.Stop();
        public void Restart() => sw.Restart();
        public TimeSpan Elapsed => sw.Elapsed;
        public long ElapsedMilliseconds => sw.ElapsedMilliseconds;
        public bool IsRunning => sw.IsRunning;
    }
}
