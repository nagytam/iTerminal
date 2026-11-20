using iTerminal.Core.Services.Sharing;
using Moq;

namespace iTerminal.Core.Test.Services.Sharing;

/// <summary>Start and dispose paths that never reach a socket: the address lookup fails, or the share is disposed first.</summary>
[TestClass]
public class FileShareLifecycleTest
{
    private static string ExistingFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "iTerminal-lifecycle-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        return path;
    }

    [TestMethod]
    public async Task StartWhenTheAddressLookupFails_ReportsFailedInsteadOfStayingStarting()
    {
        Mock<IFileShareAddressSource> source = new();
        source.Setup(s => s.Candidates()).Throws(new InvalidOperationException("no interfaces"));
        using FileShareService service = new(source.Object);
        string path = ExistingFile();
        try
        {
            FileShareStartResult result = await service.StartAsync([path]);

            Assert.IsFalse(result.Started);
            Assert.AreEqual("FileTransfer.BindFailed", result.FailureKey);
            Assert.AreEqual(FileShareStatus.Failed, service.State.Status, "the state is not left at Starting");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Dispose_OfAStoppedShare_ReportsStopped()
    {
        Mock<IFileShareAddressSource> source = new();
        FileShareService service = new(source.Object);

        service.Dispose();

        Assert.AreEqual(FileShareStatus.Stopped, service.State.Status);
    }

    [TestMethod]
    public async Task StartAfterDispose_IsRefused()
    {
        Mock<IFileShareAddressSource> source = new();
        FileShareService service = new(source.Object);
        service.Dispose();
        string path = ExistingFile();
        try
        {
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => service.StartAsync([path]));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void Snapshot_BindsNothingItself()
    {
        // The state is a plain value: reading it never starts or stops anything.
        FileShareState state = FileShareService.Snapshot(FileShareState.Stopped, DateTimeOffset.MinValue, 0, DateTimeOffset.UtcNow);

        Assert.AreEqual(FileShareStatus.Stopped, state.Status);
    }
}
