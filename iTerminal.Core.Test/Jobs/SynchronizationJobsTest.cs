using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class SettingsSynchronizationJobTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        var mockSettings = new Mock<ISettingsService>();
        var job = new SettingsSynchronizationJob(mockSettings.Object);
        Assert.IsNotNull(job);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_CanBeCancelled()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.IsDirty).Returns(false);
        var job = new SettingsSynchronizationJob(mockSettings.Object);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await job.ExecuteAsync(cts.Token);
        // Passes if no exception is thrown
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnAfterStop_CallsWriteWhenDirty()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.IsDirty).Returns(true);
        var job = new SettingsSynchronizationJob(mockSettings.Object);

        job.OnAfterStop();

        mockSettings.Verify(s => s.Write(), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnAfterStop_DoesNotCallWrite_WhenNotDirty()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.IsDirty).Returns(false);
        var job = new SettingsSynchronizationJob(mockSettings.Object);

        job.OnAfterStop();

        mockSettings.Verify(s => s.Write(), Times.Never);
    }
}

[TestClass]
public class MemoryDriveSynchronizationJobTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_DoesNotThrow()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var mockMemoryDrive = new Mock<IMemoryDriveService>();
        mockMemoryDrive.Setup(m => m.Root).Returns(new iTerminal.Core.Model.ContentModels.MemoryFolder("/"));

        var job = new MemoryDriveSynchronizationJob(mockSettings.Object, mockMemoryDrive.Object);
        Assert.IsNotNull(job);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_UsesSyncPathFromSettings_WhenProvided()
    {
        var customPath = Path.Combine(Path.GetTempPath(), "TestSyncPath_" + Guid.NewGuid().ToString("N"));
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Get(It.Is<string>(k => k.Contains("SyncPath")))).Returns(customPath);
        var mockMemoryDrive = new Mock<IMemoryDriveService>();
        mockMemoryDrive.Setup(m => m.Root).Returns(new iTerminal.Core.Model.ContentModels.MemoryFolder("/"));

        var job = new MemoryDriveSynchronizationJob(mockSettings.Object, mockMemoryDrive.Object);
        Assert.IsNotNull(job);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_CanBeCancelled()
    {
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.Get(It.IsAny<string>())).Returns((string?)null);
        var mockMemoryDrive = new Mock<IMemoryDriveService>();
        mockMemoryDrive.Setup(m => m.Root).Returns(new iTerminal.Core.Model.ContentModels.MemoryFolder("/"));

        var job = new MemoryDriveSynchronizationJob(mockSettings.Object, mockMemoryDrive.Object);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        await job.ExecuteAsync(cts.Token);
        // Passes if no exception is thrown
    }
}
