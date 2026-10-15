using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Controls;

namespace iConsole.Commands.Test;

[TestClass]
public class CloseMenuCommandTest
{
    [TestMethod]
    public async Task ExecuteAsync_ShouldCloseOpenedMenu()
    {
        var mockTheme = new Moq.Mock<iConsole.Services.IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var child = new MenuItem("C", null, new MenuItem[] { }, mockTheme.Object);
        var parent = new MenuItem("P", null, new[] { child }, mockTheme.Object);
        parent.OpenMenu();

        var cmd = new CloseMenuCommand();
        cmd.Control = child;

        Assert.IsTrue(parent.IsOpened);

        await cmd.ExecuteAsync(null);

        Assert.IsFalse(parent.IsOpened);
    }
}

