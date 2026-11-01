using iTerminal.Core.Services;
using iTerminal.Core.Services.BackgroundStart;
using Moq;

namespace iTerminal.Core.Test.Services.BackgroundStart;

[TestClass]
public class BackgroundStartServiceTest
{
    [TestMethod]
    public void ModeIsOnDemandWhenNothingIsStored()
    {
        Assert.AreEqual(BackgroundStartMode.OnDemand, TestBackgroundStart.Unset().ModeOf(BackgroundServices.FolderIndex));
    }

    [TestMethod]
    public void ModeIsOnDemandWhenTheStoredValueIsNotAMode()
    {
        Mock<ISettingsService> settings = new();
        settings.Setup(service => service.Get(It.IsAny<string>())).Returns("sometimes");

        Assert.AreEqual(BackgroundStartMode.OnDemand, new BackgroundStartService(settings.Object).ModeOf(BackgroundServices.MusicLibrary));
    }

    [TestMethod]
    [DataRow("immediately", BackgroundStartMode.Immediately)]
    [DataRow("DELAYED", BackgroundStartMode.Delayed)]
    [DataRow("OnDemand", BackgroundStartMode.OnDemand)]
    [DataRow("2", BackgroundStartMode.OnDemand)]
    [DataRow("7", BackgroundStartMode.OnDemand)]
    public void ParsesTheStoredModeIgnoringCase(string stored, BackgroundStartMode expected)
    {
        Assert.AreEqual(expected, BackgroundStartService.ParseMode(stored));
    }

    [TestMethod]
    public void EachServiceReadsItsOwnSetting()
    {
        Mock<ISettingsService> settings = new();
        settings.Setup(service => service.Get("BackgroundStart.FolderIndex")).Returns("Immediately");
        settings.Setup(service => service.Get("BackgroundStart.MusicLibrary")).Returns("Delayed");
        BackgroundStartService registry = new(settings.Object);

        Assert.AreEqual(BackgroundStartMode.Immediately, registry.ModeOf(BackgroundServices.FolderIndex));
        Assert.AreEqual(BackgroundStartMode.Delayed, registry.ModeOf(BackgroundServices.MusicLibrary));
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task ImmediateServiceDoesNotWait()
    {
        BackgroundStartService registry = TestBackgroundStart.Configured(BackgroundStartMode.Immediately);

        await registry.WaitForStartAsync(BackgroundServices.OllamaServer, CancellationToken.None);
    }

    [TestMethod]
    public async Task OnDemandServiceWaitsForARequest()
    {
        BackgroundStartService registry = TestBackgroundStart.Configured(BackgroundStartMode.OnDemand);
        Task wait = registry.WaitForStartAsync(BackgroundServices.UpdateCheck, CancellationToken.None);

        await Task.Delay(100);
        Assert.IsFalse(wait.IsCompleted, "An on-demand service started without a request.");

        registry.Request(BackgroundServices.UpdateCheck);
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task OnDemandServiceStopsWaitingWhenCancelled()
    {
        BackgroundStartService registry = TestBackgroundStart.Configured(BackgroundStartMode.OnDemand);
        using CancellationTokenSource cancellation = new();
        Task wait = registry.WaitForStartAsync(BackgroundServices.NetworkInfo, cancellation.Token);

        cancellation.Cancel();

        try
        {
            await wait;
            Assert.Fail("A cancelled wait should not complete normally.");
        }
        catch (OperationCanceledException)
        {
        }
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task RequestIsIdempotent()
    {
        BackgroundStartService registry = TestBackgroundStart.Configured(BackgroundStartMode.OnDemand);

        registry.Request(BackgroundServices.DocumentLibrary);
        registry.Request(BackgroundServices.DocumentLibrary);

        await registry.WaitForStartAsync(BackgroundServices.DocumentLibrary, CancellationToken.None);
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task DelayedServiceStartsAfterItsDelayWithoutARequest()
    {
        BackgroundStartService registry = TestBackgroundStart.Configured(BackgroundStartMode.Delayed, TimeSpan.FromMilliseconds(150));
        Task wait = registry.WaitForStartAsync(BackgroundServices.FolderIndex, CancellationToken.None);

        await Task.Delay(20);
        Assert.IsFalse(wait.IsCompleted, "A delayed service started before its delay.");

        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    [Timeout(5000)]
    public async Task DelayedServiceStartsEarlierWhenRequested()
    {
        BackgroundStartService registry = TestBackgroundStart.Configured(BackgroundStartMode.Delayed, TimeSpan.FromMinutes(10));
        Task wait = registry.WaitForStartAsync(BackgroundServices.MusicLibrary, CancellationToken.None);

        registry.Request(BackgroundServices.MusicLibrary);

        await wait.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task DelayedServiceStopsWaitingWhenCancelled()
    {
        BackgroundStartService registry = TestBackgroundStart.Configured(BackgroundStartMode.Delayed, TimeSpan.FromMinutes(10));
        using CancellationTokenSource cancellation = new();
        Task wait = registry.WaitForStartAsync(BackgroundServices.FolderIndex, cancellation.Token);

        cancellation.Cancel();

        try
        {
            await wait;
            Assert.Fail("A cancelled wait should not complete normally.");
        }
        catch (OperationCanceledException)
        {
        }
    }
}
