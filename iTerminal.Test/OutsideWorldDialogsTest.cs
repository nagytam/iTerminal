namespace iTerminal.Test;

/// <summary>
/// The dialogs whose work happens outside the machine — the network browser, the stock browser and the package
/// manager — driven end to end against the sandbox's fakes (<see cref="SandboxServices"/>).
/// </summary>
[TestClass]
public class OutsideWorldDialogsTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task CloseAndCheckAsync()
    {
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s), "the panels to be drawn again");
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task CtrlNScansTheNetworkAndListsWhatItFinds()
    {
        Assert.IsTrue(await _app.PressAsync("Ctrl+N"));

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.NetworkBrowserDialog, "the network browser");
        await _app.WaitForTextAsync(SandboxServices.HostAddress.ToString());
        await _app.WaitForTextAsync("photos");
        await CloseAndCheckAsync();
    }

    [TestMethod]
    public async Task CtrlShiftJListsTheBluetoothDevicesOfTheSandbox()
    {
        Assert.IsTrue(await _app.PressAsync("Ctrl+Shift+J"));

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.Bluetooth.BluetoothDeviceManagerDialog, "the Bluetooth device manager");
        await _app.WaitForTextAsync("Sandbox Headphones");
        await _app.WaitForTextAsync("Sandbox Keyboard");
        await CloseAndCheckAsync();
    }

    [TestMethod]
    public async Task AltF8ListsTheInstalledPackages()
    {
        Assert.IsTrue(await _app.PressAsync("Alt+F8"));

        await _app.WaitForTextAsync("Installed (2)");
        await _app.WaitForTextAsync("Node.js");
        await CloseAndCheckAsync();
    }

    [TestMethod]
    public async Task APackageCanBeFoundAndInstalledAfterAConfirmation()
    {
        await _app.PressAsync("Alt+F8");
        await _app.WaitForTextAsync("Installed (2)");

        await _app.PressAsync("s");
        await _app.TypeAsync("python");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Search: python (2)");

        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Install Python 3.12");
        await _app.PressAsync("y");

        await _app.WaitForTextAsync("Successfully installed");
        await _app.WaitForTextAsync("Done.");
    }

    [TestMethod]
    public async Task AnyKeyButYDeclinesTheInstall()
    {
        await _app.PressAsync("Alt+F8");
        await _app.WaitForTextAsync("Installed (2)");
        await _app.PressAsync("s");
        await _app.TypeAsync("python");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Search: python (2)");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Install Python 3.12");

        await _app.PressAsync("n");

        await _app.WaitForTextGoneAsync("Install Python 3.12");
        Assert.IsFalse(_app.Screen.Contains("Successfully installed"));
    }

    [TestMethod]
    public async Task AltF7OpensTheStockBrowserWithTheDefaultStocksQuoted()
    {
        Assert.IsTrue(await _app.PressAsync("Alt+F7"));

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.Stocks.StockBrowserDialog, "the stock browser");
        await _app.WaitForTextAsync("SPCX");
        await _app.WaitForTextAsync("Sandbox");
        foreach (var symbol in new[] { "AAPL", "MSFT", "TSLA", "GOOGL", "NVDA", "INTC" })
        {
            await _app.WaitForTextAsync(symbol);
        }

        await _app.WaitForTextAsync("+1.01%");
        await CloseAndCheckAsync();
    }

    [TestMethod]
    public async Task AStockIsFoundOnAnExchangeAddedAndRemoved()
    {
        await _app.PressAsync("Alt+F7");
        await _app.WaitForTextAsync("+1.01%");

        await _app.PressAsync("a");
        await _app.TypeAsync("acme");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Acme Holdings plc");
        await _app.WaitForTextAsync("London");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Added ACME.L");
        await _app.WaitForTextAsync("10 on the list");

        await _app.PressAsync("d");
        await _app.WaitForTextAsync("from the list?");
        await _app.PressAsync("y");
        await _app.WaitForTextAsync("9 on the list");
        await CloseAndCheckAsync();
    }
}
