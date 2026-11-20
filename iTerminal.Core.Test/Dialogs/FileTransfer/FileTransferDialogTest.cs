using System.Text;
using iConsole.Data;
using iConsole.Qr;
using iTerminal.Core.Dialogs.FileTransfer;
using iTerminal.Core.Services.Sharing;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.FileTransfer;

[TestClass]
public class FileTransferDialogTest
{
    private const string Url = "http://192.168.1.20:8080/TestToken123/";

    private static FileTransferDialog Create(Mock<IFileShareService> service) =>
        new(service.Object, TestTheme());

    private static Mock<IFileShareService> Service(FileShareState state)
    {
        Mock<IFileShareService> service = new();
        service.Setup(s => s.State).Returns(state);
        service.Setup(s => s.StartAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileShareStartResult(true, Url, null));
        service.Setup(s => s.StopAsync()).Returns(Task.CompletedTask);
        return service;
    }

    [TestMethod]
    public void Begin_WithNoSelection_ShowsTheReasonAndStartsNothing()
    {
        Mock<IFileShareService> service = Service(FileShareState.Stopped);
        FileTransferDialog dialog = Create(service);

        dialog.Begin([]);

        Assert.AreEqual("Select a file or folder first.", dialog.StatusText);
        service.Verify(s => s.StartAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void Begin_WithAFileOnAnotherDrive_IsRefused()
    {
        Mock<IFileShareService> service = Service(FileShareState.Stopped);
        FileTransferDialog dialog = Create(service);

        dialog.Begin([new Url("ftp://example.com/share/file.txt")]);

        Assert.AreEqual("Only files on this computer can be shared.", dialog.StatusText);
        service.Verify(s => s.StartAsync(It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Begin_WithALocalFile_StartsTheShareOnTheBackground()
    {
        string path = Path.Combine(Path.GetTempPath(), "iTerminal-dialog-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, "x");
        try
        {
            Mock<IFileShareService> service = Service(FileShareState.Stopped);
            FileTransferDialog dialog = Create(service);

            dialog.Begin([new Url(path)]);

            await WaitUntilAsync(() => service.Invocations.Any(call => call.Method.Name == nameof(IFileShareService.StartAsync)));
            service.Verify(s => s.StartAsync(It.Is<IReadOnlyList<string>>(paths => paths.Single() == path), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void OnIdle_WhenRunning_ShowsTheAddressAndTheCode()
    {
        bool[,] modules = QrEncoder.Encode(Encoding.UTF8.GetBytes(Url));
        FileShareState running = new(FileShareStatus.Running, Url, modules, 0, null);
        FileTransferDialog dialog = Create(Service(running));

        dialog.OnIdle();

        Assert.AreEqual(Url, dialog.UrlText);
        Assert.AreEqual(QrTextRenderer.Render(modules).Count, dialog.CodeView.ViewSize.Height, "the code has one line per two module rows");
    }

    [TestMethod]
    public void OnIdle_WhenTheBindFails_ShowsTheReason()
    {
        FileShareState failed = new(FileShareStatus.Failed, null, null, 0, "FileTransfer.BindFailed");
        FileTransferDialog dialog = Create(Service(failed));

        dialog.OnIdle();

        Assert.AreEqual("No port of 8080 to 8089 is free, or no LAN address was found.", dialog.StatusText);
        Assert.AreEqual(string.Empty, dialog.UrlText);
    }

    [TestMethod]
    public async Task Escape_StopsTheShareAndCloses()
    {
        Mock<IFileShareService> service = Service(FileShareState.Stopped);
        FileTransferDialog dialog = Create(service);

        bool handled = await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(handled);
        await WaitUntilAsync(() => service.Invocations.Any(call => call.Method.Name == nameof(IFileShareService.StopAsync)));
        service.Verify(s => s.StopAsync(), Times.Once);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }
}
