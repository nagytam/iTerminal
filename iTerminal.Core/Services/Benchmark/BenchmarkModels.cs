namespace iTerminal.Core.Services.Benchmark;

/// <summary>The groups the benchmark measures. The overall score is built from these.</summary>
public enum BenchmarkCategory
{
    Cpu,
    Memory,
    Disk,
    Network,
    Latency,
}

/// <summary>
/// What one probe measured. <see cref="Value"/> is null when the probe could not measure (no network, no file to read);
/// <see cref="Reason"/> then says why. <see cref="Reference"/> is the value a reference machine gets — a score of 1000.
/// </summary>
public sealed record BenchmarkMeasurement(
    double? Value,
    string Unit,
    double Reference,
    bool HigherIsBetter,
    string? Reason = null)
{
    public static BenchmarkMeasurement Unavailable(string unit, double reference, bool higherIsBetter, string reason) =>
        new(null, unit, reference, higherIsBetter, reason);
}

/// <summary>One probe's outcome: its measurement and the score derived from it (null when it could not measure).</summary>
public sealed record BenchmarkResult(
    string Id,
    string Name,
    BenchmarkCategory Category,
    BenchmarkMeasurement Measurement)
{
    public double? Score => BenchmarkScore.ForMeasurement(Measurement);
}

/// <summary>What a run produced. <see cref="Overall"/> is null when no probe could be scored.</summary>
public sealed record BenchmarkReport(
    IReadOnlyList<BenchmarkResult> Results,
    bool IsComplete)
{
    /// <summary>The geometric mean of the category scores, so one strong category cannot hide a weak one.</summary>
    public double? Overall => BenchmarkScore.Combine(CategoryScores.Values);

    /// <summary>The score of each category that has at least one scored probe, from the probes' geometric mean.</summary>
    public IReadOnlyDictionary<BenchmarkCategory, double> CategoryScores =>
        Results.Where(result => result.Score is not null)
            .GroupBy(result => result.Category)
            .ToDictionary(group => group.Key, group => BenchmarkScore.Combine(group.Select(result => result.Score!.Value)) ?? 0);
}

/// <summary>A step of a run: the probe that is starting (<see cref="Result"/> null), or the one that has just finished.</summary>
public sealed record BenchmarkProgress(int Index, int Total, string Name, BenchmarkResult? Result = null);

/// <summary>Scores are relative to a reference machine: 1000 is what it measures, twice as fast is 2000.</summary>
public static class BenchmarkScore
{
    /// <summary>The score the reference machine gets.</summary>
    public const double ReferenceScore = 1000;

    /// <summary>
    /// The score of a measurement, or null when there is none. A higher-is-better value scores in proportion to the
    /// reference; a lower-is-better one (a latency) in inverse proportion, so a score always grows with speed.
    /// </summary>
    public static double? ForMeasurement(BenchmarkMeasurement measurement)
    {
        if (measurement.Value is not { } value || !double.IsFinite(value) || value <= 0 || measurement.Reference <= 0)
        {
            return null;
        }

        double ratio = measurement.HigherIsBetter ? value / measurement.Reference : measurement.Reference / value;
        return ReferenceScore * ratio;
    }

    /// <summary>The geometric mean of the scores, or null when there are none.</summary>
    public static double? Combine(IEnumerable<double> scores)
    {
        List<double> values = [.. scores.Where(score => double.IsFinite(score) && score > 0)];
        if (values.Count == 0)
        {
            return null;
        }

        return Math.Exp(values.Average(Math.Log));
    }
}
