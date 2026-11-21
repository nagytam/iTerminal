using iTerminal.Core.Services.Bluetooth;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Bluetooth.Test;

[TestClass]
public class WindowsBluetoothParserTest
{
    // Recorded from `Get-PnpDevice -Class Bluetooth` on Windows 11 through WindowsBluetoothParser.ListScript; the names
    // and addresses are made up, the shape is as printed. The same phone appears twice (BTHLE and BTHENUM), one device
    // name has a typographic apostrophe, and one node is not present.
    private const string Recorded =
        """
        [{"InstanceId":"BTHENUM\\DEV_AABBCCDDEE01\\7&2758E641&0&BLUETOOTHDEVICE_AABBCCDDEE01","Name":"Studio Speaker","Present":true,"Connected":true},
        {"InstanceId":"BTHLE\\DEV_112233445566\\7&F13B0B&0&112233445566","Name":"Anna’s Phone","Present":true,"Connected":false},
        {"InstanceId":"BTHENUM\\DEV_112233445566\\7&2758E641&0&BLUETOOTHDEVICE_112233445566","Name":"Anna’s Phone","Present":true,"Connected":false},
        {"InstanceId":"BTHENUM\\DEV_00169436eefe\\7&2758E641&0&BLUETOOTHDEVICE_00169436EEFE","Name":"Headset","Present":false,"Connected":false}]
        """;

    [TestMethod]
    public void TryParseDevices_ReadsRecordedOutput()
    {
        Assert.IsTrue(WindowsBluetoothParser.TryParseDevices(Recorded, out var devices));

        Assert.AreEqual(2, devices.Count, "The duplicate is listed once and the node that is not present is dropped.");
        Assert.AreEqual(new BluetoothDevice("AA:BB:CC:DD:EE:01", "Studio Speaker", IsPaired: true, IsConnected: true), devices[0]);
        Assert.AreEqual(new BluetoothDevice("11:22:33:44:55:66", "Anna’s Phone", IsPaired: true, IsConnected: false), devices[1]);
        Assert.IsTrue(devices.All(device => BluetoothctlParser.IsAddress(device.Address)));
    }

    [TestMethod]
    public void TryParseDevices_ReadsTheBareObjectPowerShellPrintsForOneResult()
    {
        const string One = """{"InstanceId":"BTHENUM\\DEV_AABBCCDDEE01\\7&2758E641","Name":"Studio Speaker","Present":true,"Connected":false}""";

        Assert.IsTrue(WindowsBluetoothParser.TryParseDevices(One, out var devices));

        Assert.AreEqual("AA:BB:CC:DD:EE:01", devices.Single().Address);
    }

    [TestMethod]
    public void TryParseDevices_NoOutputIsNoDevices()
    {
        Assert.IsTrue(WindowsBluetoothParser.TryParseDevices(" \r\n", out var devices));
        Assert.AreEqual(0, devices.Count);
    }

    [TestMethod]
    public void TryParseDevices_ANodeWithoutANameIsShownByItsAddress()
    {
        const string Unnamed = """[{"InstanceId":"BTHLE\\DEV_AABBCCDDEE01\\7","Name":null,"Present":true,"Connected":false}]""";

        Assert.IsTrue(WindowsBluetoothParser.TryParseDevices(Unnamed, out var devices));

        Assert.AreEqual("AA:BB:CC:DD:EE:01", devices.Single().Name);
    }

    [TestMethod]
    public void TryParseDevices_ServiceNodesAndForeignInstanceIdsAreIgnored()
    {
        const string Services = """
            [{"InstanceId":"BTHENUM\\{0000110E-0000-1000-8000-00805F9B34FB}_VID&0001004C\\7","Name":"Avrcp Transport","Present":true,"Connected":false},
            {"InstanceId":"USB\\VID_8087\\5","Name":"Radio","Present":true,"Connected":false}]
            """;

        Assert.IsTrue(WindowsBluetoothParser.TryParseDevices(Services, out var devices));
        Assert.AreEqual(0, devices.Count);
    }

    [TestMethod]
    public void ListScript_ForcesUtf8AndSilencesTheNoDevicesError()
    {
        StringAssert.StartsWith(WindowsBluetoothParser.ListScript, "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8;");
        StringAssert.Contains(WindowsBluetoothParser.ListScript, "Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue");
    }

    [TestMethod]
    [DataRow("Get-PnpDevice : something went wrong")]
    [DataRow("42")]
    [DataRow("{\"InstanceId\":")]
    public void TryParseDevices_TextThatIsNotTheListingFails(string text)
    {
        Assert.IsFalse(WindowsBluetoothParser.TryParseDevices(text, out var devices));
        Assert.AreEqual(0, devices.Count);
    }
}
