using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Jobs.Test;

/// <summary>The memory drive mirrors its files to a folder: the folder's own files are loaded, subfolders are not, and changes go both ways.</summary>
[TestClass]
public class MemoryDriveSynchronizationJobTest
{
    private string _syncFolder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _syncFolder = Path.Combine(Path.GetTempPath(), "MemoryDriveSync_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_syncFolder, "sub"));
        File.WriteAllText(Path.Combine(_syncFolder, "top.txt"), "top");
        File.WriteAllText(Path.Combine(_syncFolder, "sub", "nested.txt"), "nested");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_syncFolder, recursive: true);
        }
        catch
        {
        }
    }

    private MemoryDriveService NewDrive() => new();

    private MemoryDriveSynchronizationJob NewJob(IMemoryDriveService drive)
    {
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Get(It.IsAny<string>())).Returns(_syncFolder);
        return new MemoryDriveSynchronizationJob(settings.Object, drive);
    }

    /// <summary>Starts the job with a token already cancelled: it loads the folder once and exits without looping.</summary>
    private static async Task LoadAsync(MemoryDriveSynchronizationJob job)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await job.ExecuteAsync(cancelled.Token);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Load_TakesTheFoldersOwnFiles_NotThoseInItsSubfolders()
    {
        var drive = NewDrive();

        await LoadAsync(NewJob(drive));

        Assert.IsTrue(drive.Root.Files.ContainsKey("top.txt"));
        Assert.IsFalse(drive.Root.Files.ContainsKey("nested.txt"), "a subfolder's file must not appear at the root under its bare name");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnStop_AMemoryFileIsWrittenToTheFolder()
    {
        var drive = NewDrive();
        var job = NewJob(drive);
        await LoadAsync(job);
        await drive.WriteFileAsync(new iConsole.Data.Url(iConsole.Data.Container.MEMORY_DRIVE + "fresh.txt"), new MemoryStream(new byte[] { 1, 2 }));

        job.OnAfterStop();

        Assert.IsTrue(File.Exists(Path.Combine(_syncFolder, "fresh.txt")));
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(_syncFolder, "fresh.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnStop_AFileRemovedInMemory_LeavesTheFolderToo()
    {
        var drive = NewDrive();
        var job = NewJob(drive);
        await LoadAsync(job);
        Assert.IsTrue(drive.Root.DeleteFile(new iConsole.Data.Url(iConsole.Data.Container.MEMORY_DRIVE + "top.txt")));

        job.OnAfterStop();

        Assert.IsFalse(File.Exists(Path.Combine(_syncFolder, "top.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnStop_WhenTheFolderCannotBeWritten_DoesNotThrow()
    {
        var drive = NewDrive();
        var job = NewJob(drive);
        await LoadAsync(job);
        await drive.WriteFileAsync(new iConsole.Data.Url(iConsole.Data.Container.MEMORY_DRIVE + "fresh.txt"), new MemoryStream(new byte[] { 1 }));
        Directory.Delete(_syncFolder, recursive: true);
        File.WriteAllText(_syncFolder, "a file where the folder was");

        job.OnAfterStop();

        Assert.IsTrue(File.Exists(_syncFolder), "a folder that cannot be written is traced, not a crash at quit");
    }
}
