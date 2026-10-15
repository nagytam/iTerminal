using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class SettingsNetworkTabPageTest
{
    [TestMethod]
    public void PointsAtTheNetworkBrowserInsteadOfStandingEmpty()
    {
        var page = new SettingsNetworkTabPage(TestTheme());

        var labels = page.Controls.AsSnapshot().OfType<Label>().Select(l => l.Text).ToList();

        Assert.IsTrue(labels.Any(l => l.Contains("Network browser (Ctrl+N)")));
    }

    [TestMethod]
    public void HotKeyIsTwo()
    {
        Assert.AreEqual(ConsoleKey.D2, new SettingsNetworkTabPage(TestTheme()).HotConsoleKey);
    }
}
