using Microsoft.VisualStudio.TestTools.UnitTesting;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class SystemInfoDialogTest
{
    [TestMethod]
    public async Task OpensAtOnceAndFillsInTheReportInTheBackground()
    {
        // The report walks every drive and network interface; a disconnected network drive can
        // hold that for seconds, and it used to hold the dialog from appearing at all.
        var dialog = new SystemInfoDialog(TestTheme());

        await ShowAsync(dialog);
        await dialog.InfoTask;

        var lines = dialog.LinesForTest;
        Assert.IsFalse(lines.Contains(SystemInfoDialog.CollectingText), "the placeholder is replaced");
        Assert.IsTrue(lines.Any(l => l.Contains("Operating System")));
        Assert.IsTrue(lines.Any(l => l.Contains("Processor Count")));
    }

    [TestMethod]
    public void TheReportCoversEachSection()
    {
        var report = SystemInfoDialog.BuildSystemInfo();

        foreach (var section in new[] { "Operating System", "Hardware", "Runtime", "Current User", "Drives", "Network Interfaces", "Environment Variables" })
        {
            StringAssert.Contains(report, section);
        }
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = new SystemInfoDialog(TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
        await dialog.InfoTask;
    }
}
