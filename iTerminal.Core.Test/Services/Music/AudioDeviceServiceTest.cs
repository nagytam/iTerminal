using iTerminal.Core.Audio;
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

        public bool IsAvailable() => ThrowOnProbe ? throw new InvalidOperationException("probe blew up") : available;

        public IReadOnlyList<AudioDevice> GetDevices()
        {
            DeviceQueryCount++;
            return [new AudioDevice("0", $"{Name} device", Name)];
        }

        public IAudioSink CreateSink() => new FakeAudioSink();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ResolvesTheAvailableBackendWithTheLowestPriorityNumber()
    {
        var service = new AudioDeviceService(
        [
            new StubBackend("Fallback", 20, available: true),
            new StubBackend("Primary", 10, available: true),
        ]);

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

        Assert.AreEqual("Fallback", service.ActiveBackendName);
        Assert.IsTrue(service.HasAudioOutput);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABackendThatThrowsWhileProbingIsNotACandidate()
    {
        var broken = new StubBackend("Broken", 0, available: true) { ThrowOnProbe = true };
        var service = new AudioDeviceService([broken, new StubBackend("Working", 10, available: true)]);

        Assert.AreEqual("Working", service.ActiveBackendName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithNoUsableBackend_ReportsNoOutputAndCreatesNoSink()
    {
        var service = new AudioDeviceService([new StubBackend("None", 0, available: false)]);

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

        Assert.IsFalse(service.HasAudioOutput);
        Assert.IsNull(service.CreateSink());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetDevices_IsCachedUntilRefresh()
    {
        var backend = new StubBackend("Primary", 0, available: true);
        var service = new AudioDeviceService([backend]);

        service.GetDevices();
        service.GetDevices();
        Assert.AreEqual(1, backend.DeviceQueryCount, "Repeated reads must not re-enumerate");

        service.Refresh();
        service.GetDevices();
        Assert.AreEqual(2, backend.DeviceQueryCount, "Refresh must pick up hot-plugged devices");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CreateSink_ReturnsASinkFromTheActiveBackend()
    {
        var service = new AudioDeviceService([new StubBackend("Primary", 0, available: true)]);

        using var sink = service.CreateSink();

        Assert.IsNotNull(sink);
    }
}
