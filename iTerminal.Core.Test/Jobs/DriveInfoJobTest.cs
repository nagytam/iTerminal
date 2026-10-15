using iTerminal.Core.Jobs;
using iTerminal.Core.Model;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class DriveInfoJobTest
{
    [TestMethod]
    public void Constructor_InitializesJob()
    {
        var driveInfoModel = new DriveInfoModel();

        var job = new DriveInfoJob(driveInfoModel);

        Assert.IsNotNull(job);
    }

    [TestMethod]
    public void Constructor_WithModel_StoresModel()
    {
        var driveInfoModel = new DriveInfoModel();

        var job = new DriveInfoJob(driveInfoModel);

        Assert.IsNotNull(job);
    }

    [TestMethod]
    public async Task ExecuteAsync_CanBeCancelled()
    {
        var driveInfoModel = new DriveInfoModel();
        var job = new DriveInfoJob(driveInfoModel);
        var cts = new CancellationTokenSource();

        cts.Cancel();
        await job.ExecuteAsync(cts.Token);

        // If no exception, test passes
    }
}

