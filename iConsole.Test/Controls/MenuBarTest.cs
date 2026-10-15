using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls;
using iConsole.Services;
using iConsole.Test.Core;
using iConsole.Data;

namespace iConsole.Controls.Test;

[TestClass]
public class MenuBarTest
{
    [TestMethod]
    public void Constructor_ShouldBeVisible()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var menu = new MenuBar(new MenuItem[] { }, mockTheme.Object);

        Assert.IsTrue(menu.IsVisible);
        Assert.AreEqual("MenuBar", menu.Name);
    }

    [TestMethod]
    public void Draw_ShouldRenderItems()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var items = new[] { new MenuItem("X", null, new MenuItem[] { }, mockTheme.Object) };
        var menu = new MenuBar(items, mockTheme.Object)
        {
            ViewSize = new Size(10, 1),
            Position = new Position(0, 0)
        };

        var buffer = new MockBuffer(new Size(10, 1));
        menu.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("X"));
    }
}

