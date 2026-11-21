using iConsole.Media.Audio;
using iTerminal.Core.Data.Music;
using iTerminal.Core.Services.Music;

namespace iTerminal.Core.Test.Services.Music;

[TestClass]
public class AudioDeviceServiceTest
{
    private sealed class StubBackend(string name, int priority, bool available) : IAudioOutputBackend
    {
        public string Name { get; } = name;
        public int Priority { get; } = priority;
        public bool ThrowOnProbe { get; set; }
        public int DeviceQueryCount { get; private set; }

        /// <summary>When set, probing waits here, so a test can hold a probe open.</summary>
        public ManualResetEventSlim? ProbeGate { get; set; }

        public bool IsAvailable()
        {
            ProbeGate?.Wait(TimeSpan.FromSeconds(30));
            return ThrowOnProbe ? throw new InvalidOperationException("probe blew up") : available;
        }

        public IReadOnlyList<AudioDevice> GetDevices()
        {
            DeviceQueryCount++;
            return [new AudioDevice("0", $"{Name} device", Name)];
        }

        public IAudioSink CreateSink() => new FakeAudioSink();
    }

    /// <summary>
    /// Starts a probe with <paramref name="start"/> and waits for it to land. Subscribing before
    /// starting means the event cannot be missed.
    /// </summary>
    private static void AwaitProbe(AudioDeviceService service, Action start)
    {
        using var landed = new ManualResetEventSlim(false);
        void OnLanded(object? sender, EventArgs e) => landed.Set();

        service.DevicesChanged += OnLanded;
        try
        {
            start();
            Assert.IsTrue(landed.Wait(TimeSpan.FromSeconds(30)), "The background probe did not land.");
        }
        finally
        {
            service.DevicesChanged -= OnLanded;
        }
    }

    /// <summary>Waits for the first probe of a freshly built service, which the first read starts.</summary>
    private static void AwaitFirstProbe(AudioDeviceService service) =>
        AwaitProbe(service, () => _ = service.GetDevices());

    [Timeout(60_000)]
    [TestMethod]
    public void ResolvesTheAvailableBackendWithTheLowestPriorityNumber()
    {
        var service = new AudioDeviceService(
        [
            new StubBackend("Fallback", 20, available: true),
            new StubBackend("Primary", 10, available: true),
        ]);

        AwaitFirstProbe(service);

        Assert.AreEqual("Primary", service.ActiveBackendName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SkipsUnavailableBackends()
    {
        var service = new AudioDeviceService(
        [
            new StubBackend("Primary", 0, available: false),
            new StubBackend("Fallback", 10, available: true),
        ]);

        AwaitFirstProbe(service);

        Assert.AreEqual("Fallback", service.ActiveBackendName);
        Assert.IsTrue(service.HasAudioOutput);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABackendThatThrowsWhileProbingIsNotACandidate()
    {
        var broken = new StubBackend("Broken", 0, available: true) { ThrowOnProbe = true };
        var service = new AudioDeviceService([broken, new StubBackend("Working", 10, available: true)]);

        AwaitFirstProbe(service);

        Assert.AreEqual("Working", service.ActiveBackendName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithNoUsableBackend_ReportsNoOutputAndCreatesNoSink()
    {
        var service = new AudioDeviceService([new StubBackend("None", 0, available: false)]);

        AwaitFirstProbe(service);

        Assert.IsFalse(service.HasAudioOutput);
        Assert.AreEqual(string.Empty, service.ActiveBackendName);
        Assert.AreEqual(0, service.GetDevices().Count);
        Assert.IsNull(service.CreateSink());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithNoBackendsRegisteredAtAll_DegradesQuietly()
    {
        var service = new AudioDeviceService([]);

        AwaitFirstProbe(service);

        Assert.IsFalse(service.HasAudioOutput);
        Assert.IsNull(service.CreateSink());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetDevices_IsCachedUntilRefresh()
    {
        var backend = new StubBackend("Primary", 0, available: true);
        var service = new AudioDeviceService([backend]);

        AwaitFirstProbe(service);
        _ = service.GetDevices();
        _ = service.GetDevices();
        Assert.AreEqual(1, backend.DeviceQueryCount, "Repeated reads must not re-enumerate");

        AwaitProbe(service, service.Refresh);
        _ = service.GetDevices();
        Assert.AreEqual(2, backend.DeviceQueryCount, "Refresh must pick up hot-plugged devices");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Readers_ReturnAtOnce_WhileTheFirstProbeIsStillRunning()
    {
        using var gate = new ManualResetEventSlim(false);
        var backend = new StubBackend("Slow", 0, available: true) { ProbeGate = gate };
        var service = new AudioDeviceService([backend]);

        try
        {
            // The probe is held open on the thread pool; none of these reads may wait for it.
            Assert.AreEqual(string.Empty, service.ActiveBackendName);
            Assert.IsFalse(service.HasAudioOutput);
            Assert.AreEqual(0, service.GetDevices().Count);
            Assert.IsTrue(service.IsProbing);
        }
        finally
        {
            gate.Set();
        }

        AwaitProbeAfterGate(service);

        Assert.AreEqual("Slow", service.ActiveBackendName);
        Assert.IsFalse(service.IsProbing);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Refresh_KeepsThePreviousListUntilTheNewOneLands()
    {
        using var gate = new ManualResetEventSlim(true);
        var backend = new StubBackend("Primary", 0, available: true);
        var service = new AudioDeviceService([backend]);
        AwaitFirstProbe(service);

        gate.Reset();
        backend.ProbeGate = gate;
        service.Refresh();

        Assert.IsTrue(service.IsProbing);
        Assert.AreEqual("Primary", service.ActiveBackendName, "The old result stays readable during a rescan");
        Assert.AreEqual(1, service.GetDevices().Count);

        gate.Set();
        AwaitProbeAfterGate(service);
        Assert.IsFalse(service.IsProbing);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateSink_ReturnsASinkFromTheActiveBackend()
    {
        var service = new AudioDeviceService([new StubBackend("Primary", 0, available: true)]);

        using var sink = service.CreateSink();

        Assert.IsNotNull(sink);
    }

    /// <summary>
    /// For a probe that is already running and was gated open: waits until it lands. The event may
    /// already have been raised, so this checks the state the event announces, polled briefly.
    /// </summary>
    private static void AwaitProbeAfterGate(AudioDeviceService service)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (service.IsProbing && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.IsFalse(service.IsProbing, "The background probe did not land.");
    }
}
