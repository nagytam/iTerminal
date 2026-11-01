using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.RuntimeDependencies;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class RuntimeDependencyDialogCommandTest
{
    private static RuntimeDependencyDialogCommand CommandFor(RuntimeDependencyState state)
    {
        var service = new Mock<IRuntimeDependencyService>();
        service.SetupGet(s => s.Statuses).Returns(
        [
            new RuntimeDependencyStatus("playwright", "Playwright", "Chromium", state == RuntimeDependencyState.Installed ? "1.63.0" : null, "1.63.0", state),
        ]);
        service.SetupGet(s => s.Log).Returns(new List<string>());
        service.SetupGet(s => s.IsBusy).Returns(false);
        return new RuntimeDependencyDialogCommand(new RuntimeDependencyDialog(service.Object, TestTheme()));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCommandIsAvailable_WhenADependencyIsMissing()
    {
        Assert.IsTrue(CommandFor(RuntimeDependencyState.Missing).CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCommandIsAvailable_WhenAnUpdateIsAvailable()
    {
        Assert.IsTrue(CommandFor(RuntimeDependencyState.UpdateAvailable).CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheCommandIsUnavailable_WhenEverythingIsUpToDate()
    {
        Assert.IsFalse(CommandFor(RuntimeDependencyState.Installed).CanExecute());
    }
}
