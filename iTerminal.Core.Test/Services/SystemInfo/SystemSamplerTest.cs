using System.Diagnostics;
using iConsole.Services;
using iTerminal.Core.Services.SystemInfo;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.SystemInfo.Test;

/// <summary>
/// The sampler's rules: the loop runs while it is watched, a probe that does not answer is called once and not again
/// while it hangs, one hung device does not hold up the others, and a rate is never taken across a restart.
/// </summary>
[TestClass]
public class SystemSamplerTest
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(200);

    private FakeSystemProbes _probes = null!;
    private FakeFocus _focus = null!;
    private FakeIntervalClock _clock = null!;
    private VirtualTimeProvider _time = null!;
    private SystemSampler _sampler = null!;

    [TestInitialize]
    public void Setup()
    {
        _probes = new FakeSystemProbes();
        _focus = new FakeFocus();
        _clock = new FakeIntervalClock(Interval);
        _time = new VirtualTimeProvider();
        _sampler = Create(Budget);
    }

    [TestCleanup]
    public void Teardown()
    {
        // Release any probe still blocked on its gate, so no test leaves a thread behind.
        _probes.Gate.Set();
        _sampler.Dispose();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Watch_StartsTheLoop_AndDisposingTheLastHandleStopsIt()
    {
        IDisposable handle = _sampler.Watch();
        await NextSnapshotAsync(null);
        Assert.IsNotNull(_sampler.Current);

        handle.Dispose();
        await Task.Delay(300);
        int calls = _probes.CpuTickCalls;

        await Task.Delay(300);
        Assert.AreEqual(calls, _probes.CpuTickCalls, "no pass runs after the last handle is gone");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HungProbe_IsCalledOnce_AndRetriedOnlyAfterItReturns()
    {
        _probes.DiskList = ["sda"];
        _probes.HangingDisks.Add("sda");
        _sampler.Watch();

        SystemSnapshot first = await NextSnapshotAsync(null);
        Assert.AreEqual(0, _probes.DiskCalls("sda"), "the disk list is read in the first pass; its units start in the next");

        await _clock.ReleaseAsync();
        SystemSnapshot second = await NextSnapshotAsync(first);
        Assert.AreEqual(ProbeState.NotAnswering, DiskState(second, "sda"));
        Assert.AreEqual(1, _probes.DiskCalls("sda"));

        await _clock.ReleaseAsync();
        SystemSnapshot third = await NextSnapshotAsync(second);
        Assert.AreEqual(ProbeState.NotAnswering, DiskState(third, "sda"), "it still hangs");
        Assert.AreEqual(1, _probes.DiskCalls("sda"), "a hung probe is not called again while its call is outstanding");

        _probes.Gate.Set();
        await Task.Delay(100);
        await _clock.ReleaseAsync();
        SystemSnapshot fourth = await NextSnapshotAsync(third);
        Assert.AreEqual(2, _probes.DiskCalls("sda"), "after the abandoned call returns, the next pass calls it again");
        Assert.AreEqual(ProbeState.Measuring, DiskState(fourth, "sda"), "its first answer has no earlier read to give a rate");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OneHungDisk_DoesNotBlankTheOthers()
    {
        _probes.DiskList = ["sda", "sdb"];
        _probes.HangingDisks.Add("sdb");
        _sampler.Watch();

        SystemSnapshot first = await NextSnapshotAsync(null);

        _time.Advance(Interval);
        await _clock.ReleaseAsync();
        SystemSnapshot second = await NextSnapshotAsync(first);
        Assert.AreEqual(ProbeState.Measuring, DiskState(second, "sda"));
        Assert.AreEqual(ProbeState.NotAnswering, DiskState(second, "sdb"));

        _time.Advance(Interval);
        await _clock.ReleaseAsync();
        SystemSnapshot third = await NextSnapshotAsync(second);
        Assert.AreEqual(ProbeState.Ok, DiskState(third, "sda"), "the healthy disk still gets its rate");
        Assert.AreEqual(1000.0, DiskRate(third, "sda").ReadBytesPerSecond, 0.001, "one call's worth of bytes in one second");
        Assert.AreEqual(ProbeState.NotAnswering, DiskState(third, "sdb"));

        _probes.Gate.Set();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SeveralHungProbes_FinishThePassWithinOneBudget()
    {
        // Sequential units would take three budgets (about 900 ms here); started together, the pass takes one.
        _sampler.Dispose();
        _sampler = Create(TimeSpan.FromMilliseconds(300));
        _probes.DiskList = ["sda", "sdb", "sdc"];
        _probes.HangingDisks.Add("sda");
        _probes.HangingDisks.Add("sdb");
        _probes.HangingDisks.Add("sdc");
        _sampler.Watch();

        SystemSnapshot first = await NextSnapshotAsync(null);

        var clock = Stopwatch.StartNew();
        await _clock.ReleaseAsync();
        SystemSnapshot second = await NextSnapshotAsync(first);
        clock.Stop();

        Assert.IsTrue(clock.ElapsedMilliseconds < 800, $"the pass took {clock.ElapsedMilliseconds} ms");
        Assert.AreEqual(ProbeState.NotAnswering, DiskState(second, "sda"));
        Assert.AreEqual(ProbeState.NotAnswering, DiskState(second, "sdb"));
        Assert.AreEqual(ProbeState.NotAnswering, DiskState(second, "sdc"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARestart_MeasuresAgain_AndNeverRatesAcrossTheGap()
    {
        _probes.DiskList = ["sda"];
        IDisposable handle = _sampler.Watch();

        SystemSnapshot first = await NextSnapshotAsync(null);
        _time.Advance(Interval);
        await _clock.ReleaseAsync();
        SystemSnapshot second = await NextSnapshotAsync(first);
        _time.Advance(Interval);
        await _clock.ReleaseAsync();
        SystemSnapshot third = await NextSnapshotAsync(second);
        Assert.AreEqual(ProbeState.Ok, DiskState(third, "sda"));

        handle.Dispose();
        await Task.Delay(100);
        _time.Advance(TimeSpan.FromMinutes(5));
        _sampler.Watch();

        SystemSnapshot restarted = await NextSnapshotAsync(third);
        Assert.AreEqual(ProbeState.Measuring, DiskState(restarted, "sda"), "the first pass of a run has no rate");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Identity_IsReadOnce_UntilItAnswers()
    {
        _sampler.Watch();

        SystemSnapshot first = await NextSnapshotAsync(null);
        await _clock.ReleaseAsync();
        SystemSnapshot second = await NextSnapshotAsync(first);
        await _clock.ReleaseAsync();
        SystemSnapshot third = await NextSnapshotAsync(second);

        Assert.AreEqual(1, _probes.CpuModelCalls, "the processor model is asked for once");
        Assert.AreEqual(ProbeState.Ok, third.CpuModel.State);
        Assert.AreEqual("Test CPU", third.CpuModel.Value);
        Assert.IsTrue(first.CpuModel.State != ProbeState.Ok, "the first pass does not wait for identity");
    }

    // The fake clock tells the sample interval apart from the probe budgets by their length, so no budget may equal the
    // interval: the identity budget is 750 ms here, not one second.
    private SystemSampler Create(TimeSpan liveBudget) =>
        new(new FakeProbeFactory(_probes), _focus, _clock, _time, Interval, liveBudget, TimeSpan.FromMilliseconds(750));

    /// <summary>Waits for the sampler to publish a snapshot other than <paramref name="previous"/>.</summary>
    private async Task<SystemSnapshot> NextSnapshotAsync(SystemSnapshot? previous)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            SystemSnapshot? current = _sampler.Current;
            if (current is not null && !ReferenceEquals(current, previous))
            {
                return current;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("The sampler never published a snapshot.");
    }

    private static ProbeState DiskState(SystemSnapshot snapshot, string name) =>
        snapshot.Disks.Single(disk => disk.Name == name).Value.State;

    private static DiskRate DiskRate(SystemSnapshot snapshot, string name) =>
        snapshot.Disks.Single(disk => disk.Name == name).Value.Value!;
}
