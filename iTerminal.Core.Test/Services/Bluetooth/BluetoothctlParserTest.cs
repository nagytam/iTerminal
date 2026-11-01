using iTerminal.Core.Services.Bluetooth;
using iTerminal.Core.Services.Processes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Bluetooth.Test;

[TestClass]
public class BluetoothctlParserTest
{
    [TestMethod]
    public void ParseDevices_ReadsAddressAndNameIncludingSpaces()
    {
        const string output = "Device 50:C2:ED:36:8D:55 Jabra Evolve2 65\nDevice 00:02:3C:BD:05:E9 Sound Blaster GS3\n";

        var devices = BluetoothctlParser.ParseDevices(output);

        CollectionAssert.AreEqual(
            new[] { ("50:C2:ED:36:8D:55", "Jabra Evolve2 65"), ("00:02:3C:BD:05:E9", "Sound Blaster GS3") },
            devices.ToArray());
    }

    [TestMethod]
    public void ParseDevices_ShowsADeviceWithoutANameByItsAddress()
    {
        var devices = BluetoothctlParser.ParseDevices("Device 66:77:88:99:AA:BB\n");

        Assert.AreEqual(("66:77:88:99:AA:BB", "66:77:88:99:AA:BB"), devices.Single());
    }

    [TestMethod]
    public void ParseDevices_SkipsLinesThatAreNotDevices()
    {
        const string output = "Controller 00:1A:7D:DA:71:13 host\nDeviceSet 00:00:00:00:00:00 not available\nDevice nonsense\n";

        Assert.AreEqual(0, BluetoothctlParser.ParseDevices(output).Count);
    }

    [TestMethod]
    public void Failed_IgnoresADeviceNamedAfterAFailure()
    {
        // A listing is not a failure: a device whose name carries a failure phrase is still a device.
        var listing = new ProcessOutput(0, "Device 50:C2:ED:36:8D:55 Speaker (not available)\nDevice 66:77:88:99:AA:BB Failed to load\n", string.Empty);

        Assert.IsFalse(BluetoothctlParser.ListingFailed(listing));
        Assert.AreEqual(2, BluetoothctlParser.ParseDevices(listing.StandardOutput).Count);
    }

    [TestMethod]
    public void Failed_ReadsAnUnknownAddressButNotAName()
    {
        Assert.IsTrue(BluetoothctlParser.Failed(new ProcessOutput(0, "Device 00:00:00:00:00:00 not available\n", string.Empty)));
        Assert.IsFalse(BluetoothctlParser.Failed(new ProcessOutput(0, "Device 50:C2:ED:36:8D:55 Phone not available\n", string.Empty)));
    }

    [TestMethod]
    public void ParseState_ReadsPairedAndConnected()
    {
        const string output = "Device 50:C2:ED:36:8D:55 (public)\n\tName: Jabra\n\tPaired: yes\n\tBonded: yes\n\tConnected: no\n";

        Assert.AreEqual((true, false), BluetoothctlParser.ParseState(output));
    }

    [TestMethod]
    public void ParseState_AnUnknownDeviceIsNeitherPairedNorConnected()
    {
        Assert.AreEqual((false, false), BluetoothctlParser.ParseState("Device 00:00:00:00:00:00 not available\n"));
    }

    [TestMethod]
    public void Failed_ReadsARefusedConnectEvenWhenTheExitCodeIsZero()
    {
        var output = new ProcessOutput(0, "Attempting to connect to 50:C2:ED:36:8D:55\nFailed to connect: org.bluez.Error.Failed\n", string.Empty);

        Assert.IsTrue(BluetoothctlParser.Failed(output));
        StringAssert.Contains(BluetoothctlParser.Reason(output), "Failed to connect");
    }

    [TestMethod]
    public void Failed_IsFalseForASuccessfulConnect()
    {
        var output = new ProcessOutput(0, "Attempting to connect to 50:C2:ED:36:8D:55\nConnection successful\n", string.Empty);

        Assert.IsFalse(BluetoothctlParser.Failed(output));
    }

    [TestMethod]
    public void Failed_IsTrueWhenTheToolIsMissing()
    {
        var output = new ProcessOutput(-1, string.Empty, "bluetoothctl could not be started: No such file or directory");

        Assert.IsTrue(BluetoothctlParser.Failed(output));
        StringAssert.Contains(BluetoothctlParser.Reason(output), "No such file");
    }

    [TestMethod]
    public void Failed_IsTrueWhenThereIsNoAdapter()
    {
        var output = new ProcessOutput(0, "No default controller available\n", string.Empty);

        Assert.IsTrue(BluetoothctlParser.Failed(output));
    }

    [TestMethod]
    public void IsAddress_AcceptsOnlyTheSixByteForm()
    {
        Assert.IsTrue(BluetoothctlParser.IsAddress("50:C2:ED:36:8D:55"));
        Assert.IsFalse(BluetoothctlParser.IsAddress("50:C2:ED:36:8D"));
        Assert.IsFalse(BluetoothctlParser.IsAddress("50-C2-ED-36-8D-55"));
        Assert.IsFalse(BluetoothctlParser.IsAddress("50:C2:ED:36:8D:5Z"));
        Assert.IsFalse(BluetoothctlParser.IsAddress("50:C2:ED:36:8D:55;reboot"));
    }
}
