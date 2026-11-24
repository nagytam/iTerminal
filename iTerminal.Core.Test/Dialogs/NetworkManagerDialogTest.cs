using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Tasks;
using iConsole.Data;
using iTerminal.Core.Data;
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
    public async Task TheTableFillsTheDialog()
    {
        using var taskManager = new TaskManager();
        var dialog = new NetworkManagerDialog(taskManager, new NetworkInfoModel(), TestTheme());
        await ShowAsync(dialog);

        var table = dialog.TableForTest;

        Assert.AreEqual(dialog.ViewSize.Width - 2, table.ViewSize.Width);
        Assert.AreEqual(dialog.ViewSize.Height - 2, table.ViewSize.Height);
    }

    /// <summary>The table is bound to the model's list: an interface that arrives after the dialog opened is drawn on the next repaint.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInterfaceThatArrivesWhileOpenIsDrawnWithoutReopening()
    {
        using var taskManager = new TaskManager();
        var model = new NetworkInfoModel { IsLoaded = true };
        var dialog = new NetworkManagerDialog(taskManager, model, TestTheme());
        await ShowAsync(dialog);
        dialog.OnIdle();
        AssertScreenContains(Render(dialog), "No network interfaces found.");

        model.Networks.Add(new NetworkInfo { Name = "Loopback-Pseudo" });
        dialog.OnIdle();

        AssertScreenContains(Render(dialog), "Loopback-Pseudo");
        AssertScreenDoesNotContain(Render(dialog), "No network interfaces found.");
    }

    /// <summary>The name and the description share the width that is left, so the columns always fit the dialog.</summary>
    [Timeout(60_000)]
    [TestMethod]
    [DataRow(60)]
    [DataRow(120)]
    public async Task TheColumnsFitTheDialogWidth(int width)
    {
        using var taskManager = new TaskManager();
        var dialog = new NetworkManagerDialog(taskManager, new NetworkInfoModel(), TestTheme());
        await ShowAsync(dialog, new Size(width, 30));

        var columns = dialog.TableForTest.Columns;
        int used = columns.Sum(c => c.Width) + (columns.Count - 1);

        Assert.AreEqual(3, columns.Count);
        Assert.AreEqual(NetworkManagerDialog.StatusColumnWidth, columns[1].Width);
        Assert.AreEqual((int)dialog.TableForTest.ViewSize.Width, used);
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
}
