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

    // The Windows backend is chosen explicitly, so these run on any machine and CI on Linux exercises them too.
    private const string WindowsJson = """[{"InstanceId":"BTHENUM\\DEV_AABBCCDDEE01\\7","Name":"Speaker","Present":true,"Connected":false},{"InstanceId":"BTHENUM\\DEV_112233445566\\7","Name":"Phone","Present":true,"Connected":true}]""";

    [TestMethod]
    [Timeout(60_000)]
    public async Task Windows_ListAsync_RunsOnePowerShellAndOrdersConnectedFirst()
    {
        var runner = new FakeProcessRunner().Answer("powershell", WindowsJson);
        var service = new BluetoothService(runner, BluetoothBackend.Windows);

        var listing = await service.ListAsync();

        Assert.IsTrue(service.IsSupported);
        Assert.IsFalse(service.CanChange);
        Assert.IsNull(listing.Error);
        CollectionAssert.AreEqual(new[] { "Phone", "Speaker" }, listing.Devices.Select(device => device.Name).ToArray());
        var call = runner.Calls.Single();
        Assert.AreEqual("powershell", call.FileName);
        CollectionAssert.AreEqual(new[] { "-NoProfile", "-NonInteractive", "-Command", WindowsBluetoothParser.ListScript }, call.Arguments.ToArray());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Windows_ListAsync_ReportsWhyPowerShellFailed()
    {
        var runner = new FakeProcessRunner().Answer("powershell", string.Empty, exitCode: 1, stderr: "Get-PnpDevice : access denied");

        var listing = await new BluetoothService(runner, BluetoothBackend.Windows).ListAsync();

        Assert.AreEqual(0, listing.Devices.Count);
        StringAssert.Contains(listing.Error, "access denied");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Windows_ListAsync_ReportsOutputThatIsNotTheListing()
    {
        var runner = new FakeProcessRunner().Answer("powershell", "not json");

        var listing = await new BluetoothService(runner, BluetoothBackend.Windows).ListAsync();

        Assert.AreEqual(0, listing.Devices.Count);
        Assert.IsNotNull(listing.Error);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Windows_ChangesAreRefusedWithoutReachingPowerShell()
    {
        var runner = new FakeProcessRunner();
        var service = new BluetoothService(runner, BluetoothBackend.Windows);

        var outcomes = new[]
        {
            await service.ScanAsync(),
            await service.ConnectAsync(Headphones),
            await service.DisconnectAsync(Headphones),
            await service.PairAsync(Headphones),
            await service.RemoveAsync(Headphones),
        };

        Assert.IsTrue(outcomes.All(outcome => !outcome.Succeeded && outcome.Message.Contains("can only list")));
        Assert.AreEqual(0, runner.Calls.Count);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task NoBackend_EverythingReportsWhy()
    {
        var runner = new FakeProcessRunner();
        var service = new BluetoothService(runner, BluetoothBackend.None);

        Assert.IsFalse(service.IsSupported);
        Assert.IsFalse(service.CanChange);
        StringAssert.Contains((await service.ListAsync()).Error, "no Bluetooth backend");
        Assert.IsFalse((await service.ConnectAsync(Headphones)).Succeeded);
        Assert.AreEqual(0, runner.Calls.Count);
    }
}
