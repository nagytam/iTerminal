using iTerminal.Core.Jobs;
using iTerminal.Core.Model;
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
}
