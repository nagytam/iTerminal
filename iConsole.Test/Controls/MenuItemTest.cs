using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls;
using iConsole.Services;
using iConsole.Commands;
using iConsole.Test.Core;
using iConsole.Data;

namespace iConsole.Controls.Test;

[TestClass]
public class MenuItemTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void OpenMenu_ShouldSetIsOpenedAndMakeFirstVisible()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var child = new MenuItem("Child", null, new MenuItem[] { }, mockTheme.Object);
        var parent = new MenuItem("Parent", null, new[] { child }, mockTheme.Object);

        parent.OpenMenu();

        Assert.IsTrue(parent.IsOpened);
        Assert.IsTrue(child.IsVisible);
        Assert.IsTrue(child.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_MenuBar_ShouldRenderText()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var items = new[] { new MenuItem("A", null, new MenuItem[] { }, mockTheme.Object), new MenuItem("B", null, new MenuItem[] { }, mockTheme.Object) };
        var menuBar = new MenuBar(items, mockTheme.Object)
        {
            ViewSize = new Size(20, 1),
            Position = new Position(0, 0)
        };

        var buffer = new MockBuffer(new Size(20, 1));
        menuBar.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("A"));
        Assert.IsTrue(buffer.ContentAsString.Contains("B"));
    }
}

