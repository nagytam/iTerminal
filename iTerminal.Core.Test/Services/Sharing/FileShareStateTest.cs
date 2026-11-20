using iTerminal.Core.Services.Sharing;

namespace iTerminal.Core.Test.Services.Sharing;

[TestClass]
public class FileShareStateTest
{
    private static readonly DateTimeOffset Expiry = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Snapshot_BeforeTheCertificateExpires_IsRunning()
    {
        FileShareState running = new(FileShareStatus.Running, "https://192.168.1.20:8080/t/", null, 0, null);

        FileShareState state = FileShareService.Snapshot(running, Expiry, 3, Expiry.AddMinutes(-1));

        Assert.AreEqual(FileShareStatus.Running, state.Status);
        Assert.AreEqual(3, state.Requests, "the request count comes from the counter, not the lifecycle");
    }

    [TestMethod]
    public void Snapshot_AfterTheCertificateExpires_IsExpiredAndKeepsTheAddress()
    {
        FileShareState running = new(FileShareStatus.Running, "https://192.168.1.20:8080/t/", null, 0, null);

        FileShareState state = FileShareService.Snapshot(running, Expiry, 0, Expiry.AddSeconds(1));

        Assert.AreEqual(FileShareStatus.Expired, state.Status);
        Assert.AreEqual("FileTransfer.Expired", state.Message);
        Assert.AreEqual("https://192.168.1.20:8080/t/", state.Url);
    }

    [TestMethod]
    public void Snapshot_OfAStoppedShare_IsNeverExpired()
    {
        FileShareState stopped = FileShareState.Stopped;

        FileShareState state = FileShareService.Snapshot(stopped, Expiry, 0, Expiry.AddDays(1));

        Assert.AreEqual(FileShareStatus.Stopped, state.Status);
    }

    [TestMethod]
    [DataRow("CON", true)]
    [DataRow("con.txt", true)]
    [DataRow("NUL.log", true)]
    [DataRow("COM1", true)]
    [DataRow("lpt9.dat", true)]
    [DataRow("COM0", false)]
    [DataRow("CONSOLE", false)]
    [DataRow("notes.txt", false)]
    public void IsRefusedName_OnWindows_RefusesDeviceNames(string segment, bool refused)
    {
        Assert.AreEqual(refused, FileShareRequestHandler.IsRefusedName(segment, windows: true));
    }

    [TestMethod]
    public void IsRefusedName_OffWindows_RefusesNothing()
    {
        Assert.IsFalse(FileShareRequestHandler.IsRefusedName("CON", windows: false));
    }
}
