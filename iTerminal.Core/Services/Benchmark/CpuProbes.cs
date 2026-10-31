using System.Diagnostics;

namespace iTerminal.Core.Services.Benchmark;

/// <summary>
/// The CPU workload: a xorshift generator run for a fixed time, counted in million iterations per second. It is pure
/// arithmetic on registers, so it measures the processor and nothing else. The result is kept so the JIT cannot remove it.
/// </summary>
internal static class CpuWorkload
{
    private const int Chunk = 4_000_000;
    private static ulong _sink;

    /// <summary>Runs on this thread until <paramref name="duration"/> has passed and returns the iterations done and the seconds taken.</summary>
    internal static (long Iterations, double Seconds) Run(TimeSpan duration, CancellationToken cancellationToken)
    {
        ulong x = 0x9E3779B97F4A7C15;
        long iterations = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < Chunk; i++)
            {
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
            }

            iterations += Chunk;
        }

        stopwatch.Stop();
        Volatile.Write(ref _sink, x);
        return (iterations, stopwatch.Elapsed.TotalSeconds);
    }

    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);
}

/// <summary>One thread of the arithmetic. The reference machine runs 500 million iterations a second.</summary>
public sealed class CpuSingleThreadProbe : IBenchmarkProbe
{
    private readonly TimeSpan _duration;

    public CpuSingleThreadProbe() : this(CpuWorkload.Duration)
    {
    }

    internal CpuSingleThreadProbe(TimeSpan duration) => _duration = duration;

    public string Id => "cpu.single";

    public string Name => "Single thread";

    public BenchmarkCategory Category => BenchmarkCategory.Cpu;

    public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        (long iterations, double seconds) = CpuWorkload.Run(_duration, cancellationToken);
        return new BenchmarkMeasurement(iterations / 1e6 / seconds, "Mops/s", 500, HigherIsBetter: true);
    }, cancellationToken);
}

/// <summary>The same arithmetic on every processor at once. The reference is four reference cores, so a machine with more cores scores higher.</summary>
public sealed class CpuMultiThreadProbe : IBenchmarkProbe
{
    private readonly TimeSpan _duration;

    public CpuMultiThreadProbe() : this(CpuWorkload.Duration)
    {
    }

    internal CpuMultiThreadProbe(TimeSpan duration) => _duration = duration;

    public string Id => "cpu.multi";

    public string Name => "All threads";

    public BenchmarkCategory Category => BenchmarkCategory.Cpu;

    public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        int threads = Math.Max(1, Environment.ProcessorCount);
        long total = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { CancellationToken = cancellationToken }, _ =>
        {
            long iterations = CpuWorkload.Run(_duration, cancellationToken).Iterations;
            Interlocked.Add(ref total, iterations);
        });
        stopwatch.Stop();
        return new BenchmarkMeasurement(total / 1e6 / stopwatch.Elapsed.TotalSeconds, "Mops/s", 2000, HigherIsBetter: true);
    }, cancellationToken);
}
