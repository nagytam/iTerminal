using System.Diagnostics;

namespace iTerminal.Core.Services.Benchmark;

/// <summary>
/// How late a one-millisecond wait wakes up: the mean over a hundred waits, in milliseconds. A terminal that redraws on a timer
/// is only as smooth as this, so a long wake-up shows as stutter.
/// </summary>
public sealed class TimerWakeupProbe : IBenchmarkProbe
{
    private const int Waits = 100;

    public string Id => "latency.timer";

    public string Name => "Timer wake-up";

    public BenchmarkCategory Category => BenchmarkCategory.Latency;

    public async Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken)
    {
        double total = 0;
        for (int i = 0; i < Waits; i++)
        {
            long start = Stopwatch.GetTimestamp();
            await Task.Delay(1, cancellationToken);
            total += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        return new BenchmarkMeasurement(total / Waits, "ms", 1.1, HigherIsBetter: false);
    }
}
