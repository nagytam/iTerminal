using System.Diagnostics;
using iConsole.Services;

namespace iConsole.Test.Services;

[TestClass]
public class ProcessServiceTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void SampleListsTheProcessesOrderedById()
    {
        var samples = new ProcessService().Sample();

        Assert.IsTrue(samples.Count > 0);
        for (int i = 0; i < samples.Count - 1; i++)
        {
            Assert.IsTrue(samples[i].Id < samples[i + 1].Id);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SampleContainsTheCurrentProcessWithItsNameAndMemory()
    {
        var samples = new ProcessService().Sample();

        var current = samples.Single(s => s.Id == Environment.ProcessId);
        Assert.AreEqual(Process.GetCurrentProcess().ProcessName, current.Name);
        Assert.IsTrue(current.WorkingSetBytes > 0, "the process holds some memory");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFirstSampleReportsNoProcessorUse()
    {
        var samples = new ProcessService().Sample();

        Assert.IsTrue(samples.All(s => s.CpuPercent is null));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProcessorUseIsMeasuredBetweenSamplesAndStaysWithinRange()
    {
        var service = new ProcessService();
        service.Sample();

        var samples = service.Sample();

        var values = samples.Where(s => s.CpuPercent is not null).Select(s => s.CpuPercent!.Value).ToList();
        Assert.IsTrue(values.Count > 0);
        Assert.IsTrue(values.All(v => v is >= 0 and <= 100));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ProcessorUseShowsWorkDoneBetweenSamples()
    {
        var service = new ProcessService();
        service.Sample();

        // Keep one thread busy for a while, so this process has used processor time between the two samples.
        var watch = Stopwatch.StartNew();
        long spin = 0;
        while (watch.ElapsedMilliseconds < 300)
        {
            spin++;
        }

        var current = service.Sample().Single(s => s.Id == Environment.ProcessId);
        Assert.IsNotNull(current.CpuPercent);
        Assert.IsTrue(current.CpuPercent > 0, $"processor use {current.CpuPercent} after {spin} iterations");
    }
}
