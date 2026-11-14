using System.Net;
using System.Net.NetworkInformation;
using iTerminal.Core.Services.NetworkTools;

namespace iTerminal.Core.Test.Services.NetworkTools;

[TestClass]
public class NetworkToolScanTest
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    private static Task<TimeSpan?> NoReply(IPAddress address, CancellationToken cancellationToken) => Task.FromResult<TimeSpan?>(null);

    private static Task<TimeSpan?> EveryHostAnswers(IPAddress address, CancellationToken cancellationToken) =>
        Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(1));

    private static Task<string?> NoName(IPAddress address, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    private static NetworkToolService Service(
        Func<IPAddress, CancellationToken, Task<TimeSpan?>>? probe = null,
        Func<IPAddress, CancellationToken, Task<string?>>? lookup = null,
        TimeSpan? timeout = null) =>
        new(probe ?? NoReply, lookup ?? NoName, timeout ?? ShortTimeout);

    private static async Task<List<string>> ScanAsync(NetworkToolService service, string target, CancellationToken cancellationToken = default)
    {
        var lines = new List<string>();
        await service.RunAsync(NetworkToolKind.Scan, target, lines.Add, cancellationToken);
        return lines;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.IsTrue(condition(), "the condition was never reached");
    }

    private static bool IsHostLine(string line) => line.Contains(" time=", StringComparison.Ordinal);

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARefusedSubnetSendsNothing()
    {
        int probes = 0;
        NetworkToolService service = Service(probe: (address, cancellationToken) =>
        {
            Interlocked.Increment(ref probes);
            return NoReply(address, cancellationToken);
        });

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => ScanAsync(service, "192.168.1.0/21"));
        Assert.AreEqual(0, probes);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyLiveHostsGetALineWithItsRoundTripTime()
    {
        NetworkToolService service = Service(probe: (address, cancellationToken) =>
            Task.FromResult<TimeSpan?>(address.ToString() == "192.168.1.5" ? TimeSpan.FromMilliseconds(7) : null));

        List<string> lines = await ScanAsync(service, "192.168.1.0/24");

        Assert.AreEqual(1, lines.Count(IsHostLine), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(line => line.StartsWith("192.168.1.5", StringComparison.Ordinal) && line.Contains("time=7 ms", StringComparison.Ordinal)), string.Join("\n", lines));
        Assert.AreEqual("Scanned 254 addresses, 1 hosts answered. Hosts that block ping do not appear.", lines[^1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AtMostThirtyTwoProbesAreInFlightAtOnce()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var counter = new object();
        int inFlight = 0;
        int peak = 0;
        async Task<TimeSpan?> BlockingProbe(IPAddress address, CancellationToken cancellationToken)
        {
            lock (counter)
            {
                inFlight++;
                peak = Math.Max(peak, inFlight);
            }

            await release.Task;
            lock (counter)
            {
                inFlight--;
            }

            return null;
        }

        NetworkToolService service = Service(probe: BlockingProbe);
        Task<List<string>> run = ScanAsync(service, "10.0.0.0/24");
        try
        {
            await WaitUntilAsync(() => { lock (counter) { return inFlight == 32; } });
            await Task.Delay(50);
            lock (counter)
            {
                Assert.AreEqual(32, peak, "the sweep must reach the 32-probe limit and never pass it");
            }
        }
        finally
        {
            release.TrySetResult();
        }

        await run;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AHostWithALookupFinishedInTimeGetsItsNameOnItsLine()
    {
        NetworkToolService service = Service(
            probe: (address, cancellationToken) => Task.FromResult<TimeSpan?>(address.ToString() == "10.0.0.1" ? TimeSpan.FromMilliseconds(2) : null),
            lookup: (address, cancellationToken) => Task.FromResult<string?>("router.lan"),
            timeout: TimeSpan.FromSeconds(2));

        List<string> lines = await ScanAsync(service, "10.0.0.0/24");

        Assert.IsTrue(lines.Any(line => line.StartsWith("10.0.0.1", StringComparison.Ordinal) && line.EndsWith("router.lan", StringComparison.Ordinal)), string.Join("\n", lines));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALookupThatNeverReturnsIsGivenUpAtTheTimeoutAndTheLineHasNoName()
    {
        var hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            NetworkToolService service = Service(
                probe: (address, cancellationToken) => Task.FromResult<TimeSpan?>(address.ToString() == "10.0.0.1" ? TimeSpan.FromMilliseconds(2) : null),
                lookup: async (address, cancellationToken) => { await hang.Task; return "never"; });

            List<string> lines = await ScanAsync(service, "10.0.0.0/24");

            string host = lines.Single(IsHostLine);
            Assert.IsTrue(host.StartsWith("10.0.0.1", StringComparison.Ordinal) && !host.Contains("never", StringComparison.Ordinal), host);
            Assert.AreEqual("Scanned 254 addresses, 1 hosts answered. Hosts that block ping do not appear.", lines[^1]);
        }
        finally
        {
            hang.TrySetResult();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AtMostEightLookupsAreInFlightAtOnceEvenWhenTheyNeverReturn()
    {
        var hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var counter = new object();
        int inFlight = 0;
        int peak = 0;
        async Task<string?> HangingLookup(IPAddress address, CancellationToken cancellationToken)
        {
            lock (counter)
            {
                inFlight++;
                peak = Math.Max(peak, inFlight);
            }

            await hang.Task;
            lock (counter)
            {
                inFlight--;
            }

            return null;
        }

        try
        {
            NetworkToolService service = Service(probe: EveryHostAnswers, lookup: HangingLookup);
            List<string> lines = await ScanAsync(service, "10.0.0.0/27");

            Assert.AreEqual(8, peak, "eight lookups hold the gate, and the rest of the hosts get no name");
            Assert.AreEqual(30, lines.Count(IsHostLine), "every live host still gets its line");
            Assert.IsFalse(lines.Any(line => IsHostLine(line) && line.Contains("never", StringComparison.Ordinal)));
        }
        finally
        {
            hang.TrySetResult();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AHostWithNoFreeLookupSlotGetsItsLineWithoutAName()
    {
        var hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eightBusy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int lookupCalls = 0;
        async Task<string?> HangingLookup(IPAddress address, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref lookupCalls) == 8)
            {
                eightBusy.TrySetResult();
            }

            await hang.Task;
            return null;
        }

        // Host .9 answers only after the eight slots are taken by hung lookups, so it finds no free slot.
        async Task<TimeSpan?> ProbeAfterEightBusy(IPAddress address, CancellationToken cancellationToken)
        {
            if (address.ToString() == "10.0.0.9")
            {
                await eightBusy.Task;
            }

            return address.ToString() is "10.0.0.1" or "10.0.0.2" or "10.0.0.3" or "10.0.0.4"
                or "10.0.0.5" or "10.0.0.6" or "10.0.0.7" or "10.0.0.8" or "10.0.0.9"
                ? TimeSpan.FromMilliseconds(1)
                : null;
        }

        try
        {
            NetworkToolService service = Service(probe: ProbeAfterEightBusy, lookup: HangingLookup);
            List<string> lines = await ScanAsync(service, "10.0.0.0/24");

            Assert.AreEqual(8, lookupCalls, "a ninth host never reaches the DNS call");
            Assert.IsTrue(lines.Any(line => line.StartsWith("10.0.0.9", StringComparison.Ordinal) && IsHostLine(line)), string.Join("\n", lines));
        }
        finally
        {
            hang.TrySetResult();
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AScanOfAPrefixSkipsNetworkAndBroadcastAddresses()
    {
        NetworkToolService service = Service(probe: EveryHostAnswers);

        List<string> lines = await ScanAsync(service, "10.0.0.0/24");

        Assert.IsFalse(lines.Any(line => line.StartsWith("10.0.0.0 ", StringComparison.Ordinal) || line.StartsWith("10.0.0.255 ", StringComparison.Ordinal)));
        Assert.AreEqual(254, lines.Count(IsHostLine));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASlash31ScansBothAddressesAndASlash32ScansOne()
    {
        List<string> pair = await ScanAsync(Service(probe: EveryHostAnswers), "10.0.0.6/31");
        Assert.AreEqual("Scanned 2 addresses, 2 hosts answered. Hosts that block ping do not appear.", pair[^1]);

        List<string> single = await ScanAsync(Service(probe: EveryHostAnswers), "10.0.0.7/32");
        Assert.AreEqual("Scanned 1 addresses, 1 hosts answered. Hosts that block ping do not appear.", single[^1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AProgressLineAppearsEverySixtyFourAddresses()
    {
        List<string> lines = await ScanAsync(Service(probe: NoReply), "10.0.0.0/24");

        string[] progress = lines.Where(line => line.StartsWith("Scanned ", StringComparison.Ordinal) && line.Contains(" of ", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEquivalent(new[] { "Scanned 64 of 254 addresses.", "Scanned 128 of 254 addresses.", "Scanned 192 of 254 addresses." }, progress);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedProbeIsNoReplyForThatHostAndTheSweepContinues()
    {
        NetworkToolService service = Service(probe: (address, cancellationToken) =>
            address.ToString() == "10.0.0.1"
                ? Task.FromException<TimeSpan?>(new PingException("the system refused to send the echo request"))
                : Task.FromResult<TimeSpan?>(TimeSpan.FromMilliseconds(3)));

        List<string> lines = await ScanAsync(service, "10.0.0.0/30");

        Assert.AreEqual(1, lines.Count(IsHostLine), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(line => line.StartsWith("10.0.0.2", StringComparison.Ordinal)));
        Assert.AreEqual("Scanned 2 addresses, 1 hosts answered. Hosts that block ping do not appear.", lines[^1]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACancelledScanWritesNothingAfterItReturns()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var lines = new List<string>();
        async Task<TimeSpan?> BlockedProbe(IPAddress address, CancellationToken cancellationToken)
        {
            await release.Task;
            return TimeSpan.FromMilliseconds(1);
        }

        NetworkToolService service = Service(probe: BlockedProbe);
        Task run = service.RunAsync(NetworkToolKind.Scan, "10.0.0.0/24", lines.Add, cancellation.Token);
        try
        {
            await Task.Delay(100);
            cancellation.Cancel();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => run);

            int written = lines.Count;
            release.TrySetResult();
            await Task.Delay(200);

            Assert.AreEqual(written, lines.Count, "in-flight probes must not write into the output after the scan returned");
        }
        finally
        {
            release.TrySetResult();
        }
    }
}
