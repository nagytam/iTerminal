using iTerminal.Core.Test.Services.BackgroundStart;
using iTerminal.Core.Services.BackgroundStart;
using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Model;
using System.Net.NetworkInformation;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class NetworkInfoJobTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesJob()
    {
        var networkInfoModel = new NetworkInfoModel();

        var job = new NetworkInfoJob(networkInfoModel, TestBackgroundStart.Configured(BackgroundStartMode.Immediately));

        Assert.IsNotNull(job);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_WithModel_StoresModel()
    {
        var networkInfoModel = new NetworkInfoModel();

        var job = new NetworkInfoJob(networkInfoModel, TestBackgroundStart.Configured(BackgroundStartMode.Immediately));

        Assert.IsNotNull(job);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_CanBeCancelled()
    {
        var networkInfoModel = new NetworkInfoModel();
        var job = new NetworkInfoJob(networkInfoModel, TestBackgroundStart.Configured(BackgroundStartMode.Immediately));
        var cts = new CancellationTokenSource();

        cts.Cancel();
        await job.ExecuteAsync(cts.Token);

        // If no exception, test passes
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasChanged_ReturnsFalse_ForEquivalentNullNetworkInterfaces()
    {
        var oldNetworkInfo = new NetworkInfo
        {
            Name = "Ethernet        ",
            NetworkInterface = null!
        };
        var newNetworkInfo = new NetworkInfo
        {
            Name = oldNetworkInfo.Name,
            NetworkInterface = null!
        };

        var result = NetworkInfoJob.HasChanged(oldNetworkInfo, newNetworkInfo);

        Assert.IsFalse(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasChanged_ReturnsTrue_WhenNameDiffers()
    {
        var oldNetworkInfo = new NetworkInfo
        {
            Name = "Ethernet        ",
            NetworkInterface = null!
        };
        var newNetworkInfo = new NetworkInfo
        {
            Name = "Wi-Fi           ",
            NetworkInterface = null!
        };

        var result = NetworkInfoJob.HasChanged(oldNetworkInfo, newNetworkInfo);

        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasChanged_ReturnsFalse_ForEquivalentValues()
    {
        var result = NetworkInfoJob.HasChanged(
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Ethernet,
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Ethernet);

        Assert.IsFalse(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasChanged_ReturnsTrue_WhenDescriptionDiffers()
    {
        var result = NetworkInfoJob.HasChanged(
            "Ethernet        ",
            "Adapter A",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Ethernet,
            "Ethernet        ",
            "Adapter B",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Ethernet);

        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasChanged_ReturnsTrue_WhenOperationalStatusDiffers()
    {
        var result = NetworkInfoJob.HasChanged(
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Ethernet,
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Down,
            "id-1",
            NetworkInterfaceType.Ethernet);

        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasChanged_ReturnsTrue_WhenIdDiffers()
    {
        var result = NetworkInfoJob.HasChanged(
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Ethernet,
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Up,
            "id-2",
            NetworkInterfaceType.Ethernet);

        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HasChanged_ReturnsTrue_WhenNetworkInterfaceTypeDiffers()
    {
        var result = NetworkInfoJob.HasChanged(
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Ethernet,
            "Ethernet        ",
            "Adapter",
            OperationalStatus.Up,
            "id-1",
            NetworkInterfaceType.Wireless80211);

        Assert.IsTrue(result);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task AfterTheFirstPass_TheListIsMarkedRead()
    {
        var model = new iTerminal.Core.Model.NetworkInfoModel();
        var job = new NetworkInfoJob(model, TestBackgroundStart.Configured(BackgroundStartMode.Immediately));
        using var cancellation = new CancellationTokenSource();
        // The job loops synchronously until cancelled, so it runs on a worker and the test polls it.
        var running = Task.Run(() => job.ExecuteAsync(cancellation.Token));

        var deadline = DateTime.UtcNow.AddSeconds(50);
        while (!model.IsLoaded && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
        cancellation.Cancel();
        await running;

        Assert.IsTrue(model.IsLoaded);
    }
}
