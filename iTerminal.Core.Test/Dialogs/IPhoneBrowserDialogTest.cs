using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class IPhoneBrowserDialogTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static (IPhoneBrowserDialog Dialog, Mock<IIPhoneDeviceService> Devices, Mock<IContentModelService> ContentModels)
        CreateDialog(params IPhoneDeviceInfo[] devices)
    {
        var deviceService = new Mock<IIPhoneDeviceService>();
        deviceService.Setup(s => s.GetDevices()).Returns(devices);
        var contentModels = new Mock<IContentModelService>();

        var dialog = new IPhoneBrowserDialog(deviceService.Object, contentModels.Object, CreateThemeService());
        return (dialog, deviceService, contentModels);
    }

    private static async Task<Control> ShowAsync(IPhoneBrowserDialog dialog)
    {
        var root = new Control(CreateThemeService()) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);
        return root;
    }

    private static async Task WaitForDevicesAsync(IPhoneBrowserDialog dialog, int expectedCount, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (dialog.DevicesForTest.Count >= expectedCount)
            {
                return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("Timed out waiting for the expected devices to appear.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitializeAsync_ListsConnectedDevices()
    {
        var device = new IPhoneDeviceInfo("00008110-ABC", "Tamas's iPhone", "iPhone14,2");
        var (dialog, _, _) = CreateDialog(device);

        await ShowAsync(dialog);
        await WaitForDevicesAsync(dialog, 1);

        Assert.IsTrue(dialog.DevicesForTest.Any(d => d.Udid == "00008110-ABC"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnSelectedDevice_MountsIPhoneUrlAndCloses()
    {
        var device = new IPhoneDeviceInfo("00008110-ABC", "Tamas's iPhone", "iPhone14,2");
        var (dialog, _, contentModels) = CreateDialog(device);
        var root = await ShowAsync(dialog);
        await WaitForDevicesAsync(dialog, 1);
        dialog.SelectRowForTest(0);

        await dialog.ConnectSelectedAsyncForTest();

        contentModels.Verify(
            s => s.SetUrlAsync(It.Is<Url>(u => u.FullUrl == "iphone://00008110-ABC/")),
            Times.Once);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog), "dialog should close after mounting the device");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rescan_TriggersSecondEnumeration()
    {
        var (dialog, devices, _) = CreateDialog();
        await ShowAsync(dialog);
        await WaitForScanAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('r', ConsoleKey.R, false, false, false));

        // One scan from OnInitializeAsync, one from the 'r' key.
        await Task.Delay(50);
        devices.Verify(s => s.GetDevices(), Times.AtLeast(2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Escape_ClosesDialog()
    {
        var (dialog, _, _) = CreateDialog();
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('\x1b', ConsoleKey.Escape, false, false, false));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    private static async Task WaitForScanAsync(IPhoneBrowserDialog dialog)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dialog.IsScanningForTest && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.IsFalse(dialog.IsScanningForTest, "the scan should finish");
    }

    private static iConsole.Test.Core.ScreenBuffer Render(IPhoneBrowserDialog dialog)
    {
        var buffer = new iConsole.Test.Core.ScreenBuffer(120, 40);
        dialog.Draw(buffer);
        foreach (var child in dialog.Controls.AsSnapshot())
        {
            child.Draw(buffer);
        }

        return buffer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithNoDeviceTheListSaysWhatToDoAndTheKeysStayVisible()
    {
        // The "no devices" message used to replace the footer — taking "r:rescan" with it, just
        // when rescanning is what the user needs.
        var (dialog, _, _) = CreateDialog();
        await ShowAsync(dialog);
        await WaitForScanAsync(dialog);

        var screen = Render(dialog);

        Assert.IsTrue(screen.Contains("No iOS devices found."), screen.ToString());
        Assert.IsTrue(screen.Contains(IPhoneBrowserDialog.KeyHints), screen.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AScanErrorIsShownBesideTheKeys()
    {
        var (dialog, devices, _) = CreateDialog();
        devices.Setup(s => s.GetDevices()).Throws(new InvalidOperationException("usbmuxd not running"));
        await ShowAsync(dialog);
        await WaitForScanAsync(dialog);

        var screen = Render(dialog);

        Assert.IsTrue(screen.Contains("Device scan error: usbmuxd not running"), screen.ToString());
        Assert.IsTrue(screen.Contains("r:rescan"), screen.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARescanIsNotStartedOverOneStillRunning()
    {
        var gate = new ManualResetEventSlim(false);
        var (dialog, devices, _) = CreateDialog();
        devices.Setup(s => s.GetDevices()).Returns(() =>
        {
            gate.Wait(TimeSpan.FromSeconds(5));
            return [];
        });
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('r', ConsoleKey.R, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('r', ConsoleKey.R, false, false, false));
        gate.Set();
        await WaitForScanAsync(dialog);

        devices.Verify(s => s.GetDevices(), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedMountStaysOpenAndSaysWhy()
    {
        var device = new IPhoneDeviceInfo("00008110-ABC", "Phone", "iPhone14,2");
        var (dialog, _, contentModels) = CreateDialog(device);
        contentModels.Setup(s => s.SetUrlAsync(It.IsAny<Url>())).ThrowsAsync(new IOException("device locked"));
        var root = await ShowAsync(dialog);
        await WaitForDevicesAsync(dialog, 1);
        dialog.SelectRowForTest(0);

        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.AreEqual("Could not open Phone: device locked", dialog.StatusForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }
}
