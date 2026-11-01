using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Tasks;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class NetworkManagerDialogTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task NoInterfacesIsSaidRatherThanLeftBlank()
    {
        using var taskManager = new TaskManager();
        var model = new NetworkInfoModel();
        var dialog = new NetworkManagerDialog(taskManager, model, TestTheme());
        await ShowAsync(dialog);

        Assert.AreEqual("Network manager", dialog.Title);
        dialog.OnIdle();
        AssertScreenContains(Render(dialog), "Reading the network interfaces");

        model.IsLoaded = true;
        dialog.OnIdle();
        AssertScreenContains(Render(dialog), "No network interfaces found.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheListFillsTheDialog()
    {
        using var taskManager = new TaskManager();
        var dialog = new NetworkManagerDialog(taskManager, new NetworkInfoModel(), TestTheme());
        await ShowAsync(dialog);

        var list = dialog.Controls.AsSnapshot().OfType<iConsole.Controls.ListBox<iTerminal.Core.Data.NetworkInfo>>().Single();

        Assert.AreEqual(dialog.ViewSize.Width - 2, list.ViewSize.Width);
        Assert.AreEqual(dialog.ViewSize.Height - 2, list.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesIt()
    {
        using var taskManager = new TaskManager();
        var dialog = new NetworkManagerDialog(taskManager, new NetworkInfoModel(), TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    /// <summary>A name longer than its column is shortened so the status column stays a column.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ALongInterfaceNameKeepsTheColumnsAligned()
    {
        var shortRow = NetworkManagerDialog.FormatRow("Wi-Fi", "Up", "Adapter");
        var longRow = NetworkManagerDialog.FormatRow("Local Area Connection* 8-WFP Native MAC Layer LightWeight Filter-0000", "Up", "WAN");

        Assert.AreEqual(shortRow.IndexOf("Up", StringComparison.Ordinal), longRow.IndexOf("Up", StringComparison.Ordinal), longRow);
        Assert.AreEqual(NetworkManagerDialog.NameColumnWidth, longRow.IndexOf("Up", StringComparison.Ordinal));
    }
}