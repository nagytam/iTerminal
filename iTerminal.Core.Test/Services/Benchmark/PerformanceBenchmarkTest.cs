using iTerminal.Core.Services.Benchmark;

namespace iTerminal.Core.Test.Services.Benchmark;

[TestClass]
public class PerformanceBenchmarkTest
{
    /// <summary>A probe that answers with a fixed measurement, or waits until it is cancelled.</summary>
    internal sealed class FakeProbe(string id, BenchmarkCategory category, BenchmarkMeasurement? measurement = null, Exception? failure = null, bool waitForCancel = false) : IBenchmarkProbe
    {
        public string Id => id;

        public string Name => id;

        public BenchmarkCategory Category => category;

        public int Runs { get; private set; }

        public async Task<BenchmarkMeasurement> MeasureAsync(CancellationToken cancellationToken)
        {
            Runs++;
            if (waitForCancel)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (failure is not null)
            {
                throw failure;
            }

            return measurement!;
        }
    }

    private static BenchmarkMeasurement Measured(double value, double reference = 1000, bool higherIsBetter = true) =>
        new(value, "u", reference, higherIsBetter);

    [TestMethod]
    public void AMeasurementAtTheReferenceScoresAThousand()
    {
        Assert.AreEqual<double?>(1000, BenchmarkScore.ForMeasurement(Measured(1000)));
        Assert.AreEqual<double?>(2000, BenchmarkScore.ForMeasurement(Measured(2000)));
    }

    [TestMethod]
    public void ALowerIsBetterMeasurementScoresInverselySoASpeedUpScoresHigher()
    {
        // A latency of twice the reference is half the score; half the reference is double.
        Assert.AreEqual<double?>(500, BenchmarkScore.ForMeasurement(Measured(60, 30, higherIsBetter: false)));
        Assert.AreEqual<double?>(2000, BenchmarkScore.ForMeasurement(Measured(15, 30, higherIsBetter: false)));
    }

    [TestMethod]
    public void AMeasurementThatIsMissingOrNotAPositiveNumberHasNoScore()
    {
        Assert.IsNull(BenchmarkScore.ForMeasurement(BenchmarkMeasurement.Unavailable("u", 1000, true, "offline")));
        Assert.IsNull(BenchmarkScore.ForMeasurement(Measured(0)));
        Assert.IsNull(BenchmarkScore.ForMeasurement(Measured(double.NaN)));
        Assert.IsNull(BenchmarkScore.ForMeasurement(Measured(double.PositiveInfinity)));
    }

    [TestMethod]
    public void TheCombinedScoreIsTheGeometricMean()
    {
        Assert.AreEqual(2000, BenchmarkScore.Combine([1000, 4000])!.Value, 1e-6);
        Assert.IsNull(BenchmarkScore.Combine([]));
    }

    [TestMethod]
    public void TheOverallScoreIsTheGeometricMeanOfTheCategoriesNotOfTheProbes()
    {
        // Three CPU probes at 1000 and one memory probe at 4000: the categories are 1000 and 4000, so the overall is 2000,
        // not the 1500 an average of the four probes would give.
        var report = new BenchmarkReport(
        [
            new BenchmarkResult("a", "a", BenchmarkCategory.Cpu, Measured(1000)),
            new BenchmarkResult("b", "b", BenchmarkCategory.Cpu, Measured(1000)),
            new BenchmarkResult("c", "c", BenchmarkCategory.Cpu, Measured(1000)),
            new BenchmarkResult("d", "d", BenchmarkCategory.Memory, Measured(4000)),
        ], IsComplete: true);

        Assert.AreEqual(2000, report.Overall!.Value, 1e-6);
        Assert.AreEqual(1000, report.CategoryScores[BenchmarkCategory.Cpu], 1e-6);
        Assert.AreEqual(4000, report.CategoryScores[BenchmarkCategory.Memory], 1e-6);
    }

    [TestMethod]
    public void AnUnavailableProbeLeavesItsCategoryAndTheOverallOutOfTheScores()
    {
        var report = new BenchmarkReport(
        [
            new BenchmarkResult("a", "a", BenchmarkCategory.Network, BenchmarkMeasurement.Unavailable("ms", 30, false, "offline")),
        ], IsComplete: true);

        Assert.IsNull(report.Overall);
        CollectionAssert.DoesNotContain(report.CategoryScores.Keys.ToList(), BenchmarkCategory.Network);
    }

    [TestMethod]
    public async Task ItRunsEveryProbeInOrderAndReportsEachStartAndResult()
    {
        var first = new FakeProbe("a", BenchmarkCategory.Cpu, Measured(1000));
        var second = new FakeProbe("b", BenchmarkCategory.Memory, Measured(2000));
        var service = new PerformanceBenchmarkService([first, second]);
        var progress = new List<BenchmarkProgress>();

        BenchmarkReport report = await service.RunAsync(new Collector(progress));

        Assert.IsTrue(report.IsComplete);
        CollectionAssert.AreEqual(new[] { "a", "b" }, report.Results.Select(result => result.Id).ToArray());
        Assert.AreEqual(4, progress.Count);
        Assert.IsNull(progress[0].Result);
        Assert.AreEqual("a", progress[1].Result!.Id);
        Assert.IsNull(progress[2].Result);
        Assert.AreEqual("b", progress[3].Result!.Id);
    }

    [TestMethod]
    public async Task AProbeThatFailsIsUnavailableAndTheOthersStillRun()
    {
        var broken = new FakeProbe("broken", BenchmarkCategory.Disk, failure: new IOException("the disk fell over"));
        var working = new FakeProbe("working", BenchmarkCategory.Cpu, Measured(1000));
        var service = new PerformanceBenchmarkService([broken, working]);

        BenchmarkReport report = await service.RunAsync();

        Assert.IsTrue(report.IsComplete);
        Assert.IsNull(report.Results[0].Score);
        Assert.AreEqual("the disk fell over", report.Results[0].Measurement.Reason);
        Assert.AreEqual(1000, report.Results[1].Score!.Value, 1e-6);
        Assert.AreEqual(1, working.Runs);
    }

    [TestMethod]
    public async Task CancellingKeepsTheScoresMeasuredSoFarAndIsNotComplete()
    {
        var done = new FakeProbe("done", BenchmarkCategory.Cpu, Measured(1000));
        var slow = new FakeProbe("slow", BenchmarkCategory.Memory, waitForCancel: true);
        var never = new FakeProbe("never", BenchmarkCategory.Disk, Measured(1000));
        var service = new PerformanceBenchmarkService([done, slow, never]);
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(200));

        BenchmarkReport report = await service.RunAsync(cancellationToken: cancellation.Token);

        Assert.IsFalse(report.IsComplete);
        CollectionAssert.AreEqual(new[] { "done" }, report.Results.Select(result => result.Id).ToArray());
        Assert.AreEqual(0, never.Runs);
    }

    private sealed class Collector(List<BenchmarkProgress> into) : IProgress<BenchmarkProgress>
    {
        public void Report(BenchmarkProgress value) => into.Add(value);
    }
}
