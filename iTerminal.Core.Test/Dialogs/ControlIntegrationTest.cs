using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>The iConsole controls the app's dialogs put to use: placeholders in empty inputs, the About detail block.</summary>
[TestClass]
public class ControlIntegrationTest
{
    [TestMethod]
    public async Task ProcessManager_EmptyFilter_ShowsItsPlaceholder()
    {
        var dialog = new ProcessManagerDialog(Mock.Of<IBeepService>(), new ProcessService(), new DateTimeProviderService(), TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "process name or id");
    }

    [TestMethod]
    public async Task ProcessManager_TypedFilter_HidesItsPlaceholder()
    {
        var dialog = new ProcessManagerDialog(Mock.Of<IBeepService>(), new ProcessService(), new DateTimeProviderService(), TestTheme());
        await ShowAsync(dialog);

        dialog.FilterBoxForTest.Text = "x";

        Assert.IsFalse(Render(dialog).ToString().Contains("process name or id"));
    }

    [TestMethod]
    public async Task About_DetailBlock_IsAlignedByTheKeyValueList()
    {
        var dialog = new AboutDialog(TestTheme());
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Version   " + AboutDialog.Version);
        AssertScreenContains(screen, "System    ");
    }

    [TestMethod]
    public void About_DetailPairs_MatchTheTextLines()
    {
        foreach (var (key, value) in AboutDialog.DetailPairs)
        {
            Assert.IsTrue(AboutDialog.Lines.Any(l => l.StartsWith(key) && l.TrimEnd().EndsWith(value)), key);
        }
    }
}
