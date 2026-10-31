using iTerminal.Core.Services.Benchmark;

namespace iTerminal.Core.Test.Services.Benchmark;

[TestClass]
public class BenchmarkProbesTest
{
    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "iTerminal-benchmark-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Everything under the folder with its size and write time: what a disk probe must leave exactly as it found it.</summary>
    private static string Snapshot(string folder) =>
        string.Join("\n", Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => File.Exists(path)
                ? $"{path}|{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path).Ticks}"
                : $"{path}|dir"));

    private static void WriteFile(string path, int bytes)
    {
        byte[] content = new byte[bytes];
        new Random(7).NextBytes(content);
        File.WriteAllBytes(path, content);
    }

    [TestMethod]
    public async Task TheDiskProbesReadAFileAndLeaveTheFolderExactlyAsTheyFoundIt()
    {
        string folder = NewFolder();
        try
        {
            WriteFile(Path.Combine(folder, "program.bin"), 9 * 1024 * 1024);
            WriteFile(Path.Combine(folder, "small.txt"), 100);
            string before = Snapshot(folder);

            BenchmarkMeasurement sequential = await new DiskSequentialReadProbe(folder).MeasureAsync(CancellationToken.None);
            BenchmarkMeasurement random = await new DiskRandomReadProbe(folder).MeasureAsync(CancellationToken.None);
            BenchmarkMeasurement listing = await new DiskListingProbe(folder).MeasureAsync(CancellationToken.None);

            Assert.IsTrue((sequential.Value ?? 0) > 0, "sequential read has a figure");
            Assert.IsTrue((random.Value ?? 0) > 0, "random read has a figure");
            Assert.IsTrue((listing.Value ?? 0) > 0, "listing has a figure");
            Assert.AreEqual(before, Snapshot(folder), "no probe created, changed or removed anything");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task ADiskProbeWithNoLargeFileToReadSaysSoInsteadOfWriting()
    {
        string folder = NewFolder();
        try
        {
            WriteFile(Path.Combine(folder, "tiny.bin"), 1024);
            string before = Snapshot(folder);

            BenchmarkMeasurement sequential = await new DiskSequentialReadProbe(folder).MeasureAsync(CancellationToken.None);

            Assert.IsNull(sequential.Value);
            StringAssert.Contains(sequential.Reason!, "8 MB");
            Assert.AreEqual(before, Snapshot(folder));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public async Task TheLoopbackProbeMovesBytesOverTheLocalNetworkOnly()
    {
        BenchmarkMeasurement measurement = await new NetworkLoopbackProbe(8L * 1024 * 1024).MeasureAsync(CancellationToken.None);

        Assert.IsNotNull(measurement.Value);
        Assert.IsTrue((measurement.Value ?? 0) > 0);
        Assert.AreEqual("MB/s", measurement.Unit);
    }

    [TestMethod]
    public async Task TheInternetProbesReportUnavailableWhenTheServerCannotBeReached()
    {
        // Port 1 on the loopback address refuses connections at once, so the probes fail without any traffic leaving the machine.
        var unreachable = new Uri("http://127.0.0.1:1/");

        BenchmarkMeasurement latency = await new NetworkLatencyProbe(unreachable).MeasureAsync(CancellationToken.None);
        BenchmarkMeasurement download = await new NetworkDownloadProbe(unreachable, 1000).MeasureAsync(CancellationToken.None);

        Assert.IsNull(latency.Value);
        StringAssert.Contains(latency.Reason!, "127.0.0.1");
        Assert.IsNull(download.Value);
        StringAssert.Contains(download.Reason!, "127.0.0.1");
    }

    [TestMethod]
    public async Task TheCpuProbesMeasureAPositiveRate()
    {
        // Short runs: the figure's size is not what is checked here, only that the probe measures and reports it.
        BenchmarkMeasurement single = await new CpuSingleThreadProbe(TimeSpan.FromMilliseconds(50)).MeasureAsync(CancellationToken.None);
        BenchmarkMeasurement multi = await new CpuMultiThreadProbe(TimeSpan.FromMilliseconds(50)).MeasureAsync(CancellationToken.None);

        Assert.IsTrue((single.Value ?? 0) > 0);
        Assert.IsTrue((multi.Value ?? 0) > 0);
        Assert.AreEqual("Mops/s", single.Unit);
    }

    [TestMethod]
    public async Task TheMemoryCopyProbeMeasuresAPositiveBandwidth()
    {
        // A small buffer and a short run: the probe's figure is what is checked here, not the machine's.
        BenchmarkMeasurement copy = await new MemoryCopyProbe(4 * 1024 * 1024, TimeSpan.FromMilliseconds(50)).MeasureAsync(CancellationToken.None);

        Assert.IsTrue((copy.Value ?? 0) > 0);
        Assert.AreEqual("GB/s", copy.Unit);
    }

    [TestMethod]
    public async Task TheTimerProbeMeasuresAWakeUpInMilliseconds()
    {
        BenchmarkMeasurement timer = await new TimerWakeupProbe().MeasureAsync(CancellationToken.None);

        Assert.IsTrue((timer.Value ?? 0) > 0.5, "a one-millisecond wait takes at least about that long");
        Assert.IsFalse(timer.HigherIsBetter);
    }

    [TestMethod]
    public void EveryProbeHasAUniqueIdAndAName()
    {
        IBenchmarkProbe[] probes =
        [
            new CpuSingleThreadProbe(), new CpuMultiThreadProbe(), new MemoryCopyProbe(), new MemoryAllocationProbe(),
            new DiskSequentialReadProbe(), new DiskRandomReadProbe(), new DiskListingProbe(), new NetworkLoopbackProbe(),
            new NetworkLatencyProbe(), new NetworkDownloadProbe(), new TimerWakeupProbe(),
        ];

        Assert.AreEqual(probes.Length, probes.Select(probe => probe.Id).Distinct().Count());
        Assert.IsTrue(probes.All(probe => !string.IsNullOrWhiteSpace(probe.Name)));
    }
}
