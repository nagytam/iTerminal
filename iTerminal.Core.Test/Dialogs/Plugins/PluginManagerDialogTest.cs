using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Dialogs.Plugins;
using iTerminal.Core.Services.Plugins;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Plugins;

[TestClass]
public class PluginManagerDialogTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Load_ShowsTheManagersPlugins_AndFollowsItsVersionOnTheIdlePass()
    {
        var manager = new Mock<IPluginManagerService>();
        manager.SetupGet(m => m.Folder).Returns("C:\\plugins");
        manager.SetupGet(m => m.Version).Returns(1);
        manager.SetupGet(m => m.Plugins).Returns(
        [
            new PluginInfo("Alpha.dll", PluginState.Unloaded, 0, 0, null),
            new PluginInfo("Beta.dll", PluginState.Failed, 0, 0, "bad image"),
        ]);
        var dialog = new PluginManagerDialog(manager.Object, CreateThemeService());

        dialog.Load();

        Assert.AreEqual(2, dialog.Shown.Count);
        Assert.AreEqual("Beta.dll", dialog.Shown[1].FileName);

        manager.SetupGet(m => m.Plugins).Returns([new PluginInfo("Alpha.dll", PluginState.Loaded, 1, 0, null)]);
        manager.SetupGet(m => m.Version).Returns(2);
        dialog.OnIdle();

        PluginInfo only = Assert.ContainsSingle(dialog.Shown);
        Assert.AreEqual(PluginState.Loaded, only.State);
        Assert.AreEqual(1, only.Viewers);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Load_RescansTheFolderEachTimeTheDialogOpens()
    {
        var manager = new Mock<IPluginManagerService>();
        manager.SetupGet(m => m.Folder).Returns("C:\\plugins");
        manager.SetupGet(m => m.Plugins).Returns([]);
        var dialog = new PluginManagerDialog(manager.Object, CreateThemeService());

        dialog.Load();
        dialog.Load();

        manager.Verify(m => m.RescanAsync(), Times.Exactly(2));
    }
}
