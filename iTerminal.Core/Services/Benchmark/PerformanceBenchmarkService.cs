namespace iTerminal.Core.Services.Benchmark;

/// <summary>One measurement of the benchmark. A probe reads or computes; it never writes a file.</summary>
public interface IBenchmarkProbe
{
    string Id { get; }

    string Name { get; }

    BenchmarkCategory Category { get; }

    /// <summary>Takes the measurement. Throws <see cref="OperationCanceledException"/> when cancelled; any other failure is reported by the probe as unavailable.</summary>
    Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken);
}

public interface IPerformanceBenchmarkService
{
    /// <summary>Every probe, in the order a run measures them. The dialog lists these before a run starts.</summary>
    IReadOnlyList<IBenchmarkProbe> Probes { get; }

    /// <summary>
    /// Runs the probes one after another. <paramref name="progress"/> hears each probe start and finish. Cancelling returns
    /// what was measured so far, with <see cref="BenchmarkReport.IsComplete"/> false.
    /// </summary>
    Task<BenchmarkReport> RunAsync(IProgress<BenchmarkProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Runs the benchmark. A probe that fails (the network is down, there is no file to read) is recorded as unavailable and left
/// out of the scores, so one missing piece does not stop the others from being measured.
/// </summary>
public sealed class PerformanceBenchmarkService(IEnumerable<IBenchmarkProbe> probes) : IPerformanceBenchmarkService
{
    private readonly IReadOnlyList<IBenchmarkProbe> _probes = [.. probes];

    public IReadOnlyList<IBenchmarkProbe> Probes => _probes;

    public async Task<BenchmarkReport> RunAsync(IProgress<BenchmarkProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        List<BenchmarkResult> results = [];
        for (int index = 0; index < _probes.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IBenchmarkProbe probe = _probes[index];
            progress?.Report(new BenchmarkProgress(index, _probes.Count, probe.Name));

            BenchmarkResult result;
            try
            {
                BenchmarkMeasurement measurement = await probe.MeasureAsync(cancellationToken);
                result = new BenchmarkResult(probe.Id, probe.Name, probe.Category, measurement);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new BenchmarkReport(results, IsComplete: false);
            }
            catch (Exception exception)
            {
                result = new BenchmarkResult(probe.Id, probe.Name, probe.Category,
                    BenchmarkMeasurement.Unavailable(string.Empty, 1, true, exception.Message));
            }

            results.Add(result);
            progress?.Report(new BenchmarkProgress(index, _probes.Count, probe.Name, result));
        }

        // A probe that ignored the cancellation and returned anyway still ends the run as cancelled.
        return new BenchmarkReport(results, IsComplete: !cancellationToken.IsCancellationRequested);
    }
}
