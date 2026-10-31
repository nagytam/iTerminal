using System.Diagnostics;

namespace iTerminal.Core.Services.Benchmark;

/// <summary>
/// Copies a buffer far larger than the processor caches over and over, and reports the bytes moved per second. The buffers
/// are allocated for the run and released with it: nothing is kept and nothing is written to disk.
/// </summary>
public sealed class MemoryCopyProbe : IBenchmarkProbe
{
    internal const int DefaultBufferBytes = 64 * 1024 * 1024;

    private readonly int _bufferBytes;
    private readonly TimeSpan _duration;

    public MemoryCopyProbe() : this(DefaultBufferBytes, TimeSpan.FromSeconds(1))
    {
    }

    internal MemoryCopyProbe(int bufferBytes, TimeSpan duration)
    {
        _bufferBytes = bufferBytes;
        _duration = duration;
    }

    public string Id => "memory.copy";

    public string Name => "Copy bandwidth";

    public BenchmarkCategory Category => BenchmarkCategory.Memory;

    public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        byte[] source = GC.AllocateUninitializedArray<byte>(_bufferBytes);
        byte[] target = GC.AllocateUninitializedArray<byte>(_bufferBytes);
        source.AsSpan().Fill(1); // Touches every page before the clock starts.
        target.AsSpan().Fill(0);

        double copied = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < _duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            source.AsSpan().CopyTo(target);
            copied += _bufferBytes;
        }

        stopwatch.Stop();
        return new BenchmarkMeasurement(copied / 1e9 / stopwatch.Elapsed.TotalSeconds, "GB/s", 10, HigherIsBetter: true);
    }, cancellationToken);
}

/// <summary>How many small objects the allocator hands out a second. Each one is dropped at once, so the collector has to keep up.</summary>
public sealed class MemoryAllocationProbe : IBenchmarkProbe
{
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(1);
    private static byte[]? _sink;

    public string Id => "memory.alloc";

    public string Name => "Allocation rate";

    public BenchmarkCategory Category => BenchmarkCategory.Memory;

    public Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        long allocations = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < Duration)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = 0; i < 100_000; i++)
            {
                Volatile.Write(ref _sink, new byte[64]);
            }

            allocations += 100_000;
        }

        stopwatch.Stop();
        return new BenchmarkMeasurement(allocations / 1e6 / stopwatch.Elapsed.TotalSeconds, "M allocs/s", 50, HigherIsBetter: true);
    }, cancellationToken);
}
