using iTerminal.Core.Services.Bluetooth;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Bluetooth.Test;

[TestClass]
public class BluetoothServiceTest
{
    private const string Headphones = "50:C2:ED:36:8D:55";
    private const string Keyboard = "66:77:88:99:AA:BB";

    [TestMethod]
    [Timeout(60_000)]
    public async Task ListAsync_OrdersConnectedThenPairedThenNearby()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The BlueZ backend runs on Linux only.");
        }

        var runner = new FakeProcessRunner()
            .Answer("bluetoothctl devices", $"Device {Keyboard} Keyboard\nDevice {Headphones} Headphones\n")
            .Answer($"bluetoothctl info {Headphones}", "\tPaired: yes\n\tConnected: yes\n")
            .Answer($"bluetoothctl info {Keyboard}", "\tPaired: no\n\tConnected: no\n");

        var listing = await new BluetoothService(runner).ListAsync();

        Assert.IsNull(listing.Error);
        Assert.AreEqual(2, listing.Devices.Count);
        Assert.AreEqual(new BluetoothDevice(Headphones, "Headphones", IsPaired: true, IsConnected: true), listing.Devices[0]);
        Assert.AreEqual(new BluetoothDevice(Keyboard, "Keyboard", IsPaired: false, IsConnected: false), listing.Devices[1]);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ListAsync_ReportsWhyTheToolCannotRun()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The BlueZ backend runs on Linux only.");
        }

        var runner = new FakeProcessRunner()
            .Answer("bluetoothctl devices", string.Empty, exitCode: -1, stderr: "bluetoothctl could not be started: No such file or directory");

        var listing = await new BluetoothService(runner).ListAsync();

        Assert.AreEqual(0, listing.Devices.Count);
        StringAssert.Contains(listing.Error, "No such file");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConnectAsync_ARefusedConnectIsAFailureWithTheReason()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The BlueZ backend runs on Linux only.");
        }

        var runner = new FakeProcessRunner()
            .Answer($"bluetoothctl connect {Headphones}", "Attempting to connect to 50:C2:ED:36:8D:55\nFailed to connect: org.bluez.Error.Failed\n");

        var outcome = await new BluetoothService(runner).ConnectAsync(Headphones);

        Assert.IsFalse(outcome.Succeeded);
        StringAssert.Contains(outcome.Message, "Failed to connect");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConnectAsync_ASuccessfulConnectSucceeds()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The BlueZ backend runs on Linux only.");
        }

        var runner = new FakeProcessRunner()
            .Answer($"bluetoothctl connect {Headphones}", "Connection successful\n");

        var outcome = await new BluetoothService(runner).ConnectAsync(Headphones);

        Assert.IsTrue(outcome.Succeeded);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ConnectAsync_RefusesAnythingButAMacAddress()
    {
        var runner = new FakeProcessRunner();

        var outcome = await new BluetoothService(runner).ConnectAsync("50:C2:ED:36:8D:55;reboot");

        Assert.IsFalse(outcome.Succeeded);
        Assert.AreEqual(0, runner.Calls.Count, "Nothing reached the tool.");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task ScanAsync_ListensForTheScanWindowThenStops()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The BlueZ backend runs on Linux only.");
        }

        var runner = new FakeProcessRunner().Answer("bluetoothctl --timeout", string.Empty);

        var outcome = await new BluetoothService(runner).ScanAsync();

        Assert.IsTrue(outcome.Succeeded);
        Assert.AreEqual("bluetoothctl --timeout 8 scan on", runner.Calls.Single().CommandLine);
    }
}
