using iTerminal.Core.Jobs;
using iTerminal.Core.Model;
using iTerminal.Core.Services.Cloud;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Jobs.Test;

/// <summary>The disk list is marked read once the first pass is over, and the memory drive is always in it.</summary>
[TestClass]
public class DriveInfoJobTest
{
    [TestMethod]
    [Timeout(60_000)]
    public async Task AfterTheFirstPass_TheListIsMarkedRead_AndHoldsTheMemoryDrive()
    {
        var model = new DriveInfoModel();
        var job = new DriveInfoJob(model);
        using var cancellation = new CancellationTokenSource();
        // The job loops synchronously until cancelled, so it runs on a worker and the test polls it.
        var running = Task.Run(() => job.ExecuteAsync(cancellation.Token));

        // A deadline, not a fixed wait: the first pass waits on every drive, and a sleeping disk can be slow.
        var deadline = DateTime.UtcNow.AddSeconds(50);
        while (!model.IsLoaded && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }
        cancellation.Cancel();
        await running;

        Assert.IsTrue(model.IsLoaded);
        Assert.IsTrue(model.Drives.Any(d => d.Name.StartsWith(iConsole.Data.Container.MEMORY_DRIVE)));
    }

    private static string NotConfiguredText => iConsole.Localization.UiText.Get("Drive.CloudNotConfigured", "Cloud sign-in not configured");

    /// <summary>Runs the job until its first pass is over, then stops it.</summary>
    private static async Task RunFirstPassAsync(DriveInfoJob job, DriveInfoModel model)
    {
        using var cancellation = new CancellationTokenSource();
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

    [TestMethod]
    [Timeout(60_000)]
    public async Task CloudRows_AreListedOnce_AndEnabledOnlyWhereTheirRegistrationIsSet()
    {
        var model = new DriveInfoModel();
        var options = Options.Create(new CloudOptions { OneDrive = new OAuthClientOptions { ClientId = "onedrive-app" } });
        var job = new DriveInfoJob(model, options);

        await RunFirstPassAsync(job, model);

        var oneDrive = model.Drives.Single(d => d.IsCloud && d.Url.ContainerType == iConsole.Data.ContainerType.OneDrive);
        var googleDrive = model.Drives.Single(d => d.IsCloud && d.Url.ContainerType == iConsole.Data.ContainerType.GoogleDrive);
        Assert.IsTrue(oneDrive.IsEnabled);
        Assert.IsFalse(oneDrive.Name.Contains(NotConfiguredText));
        Assert.IsFalse(googleDrive.IsEnabled, "Google Drive needs its client secret as well as its id.");
        Assert.IsTrue(googleDrive.Name.Contains(NotConfiguredText));
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task WithoutAConfiguration_TheCloudRowsAreStillListed_AsNotSetUp()
    {
        var model = new DriveInfoModel();
        var job = new DriveInfoJob(model);

        await RunFirstPassAsync(job, model);

        var cloudRows = model.Drives.Where(d => d.IsCloud).ToList();
        Assert.AreEqual(2, cloudRows.Count);
        Assert.IsTrue(cloudRows.All(d => !d.IsEnabled && d.Name.Contains(NotConfiguredText)));
    }
}
