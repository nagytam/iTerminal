using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.MenuBar;

[TestClass]
public class MenuBarTest
{
    [TestMethod]
    public void MenuBarIsVisibleByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var menuBar = new iConsole.Controls.MenuBar([], mockThemeService.Object);

        Assert.IsTrue(menuBar.IsVisible);
        Assert.AreEqual(nameof(iConsole.Controls.MenuBar), menuBar.Name);
    }

    [TestMethod]
    public async Task MenuBar()
    {
        #region 
        var mockBuffer = new MockBuffer(new(80, 20));
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var exitFileMenuItem = new MenuItem("Exit", null, [], mockThemeService.Object) { HotKeyChar = 'E' };
        var fileMenuItem = new MenuItem("File", null, [exitFileMenuItem], mockThemeService.Object) { HotKeyChar = 'F' };
        var aboutAboutMenuItem = new MenuItem("About Dialog", null, [], mockThemeService.Object) { HotKeyChar = 'D' };
        var aboutMenuItem = new MenuItem("About", null, [aboutAboutMenuItem], mockThemeService.Object) { HotKeyChar = 'A' };

        var window = new Window(mockThemeService.Object)
        {
            ViewSize = new Size(80, 20)
        };
        var menuBar = new iConsole.Controls.MenuBar([fileMenuItem, aboutMenuItem], mockThemeService.Object);
        window.AddControl(menuBar);
        await menuBar.OnInitializeAsync();

        Assert.IsNotNull(menuBar);
        Assert.IsFalse(menuBar.IsOpened);
        Assert.IsFalse(fileMenuItem.IsOpened);
        Assert.IsFalse(exitFileMenuItem.IsOpened);
        Assert.IsFalse(aboutMenuItem.IsOpened);
        Assert.IsFalse(aboutAboutMenuItem.IsOpened);
        menuBar.Draw(mockBuffer);
        var content = mockBuffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(content[0].TrimEnd().StartsWith("File About"));
        #endregion

        #region
        var isHandled = await menuBar.HandleKeyEventAsync(new KeyEvent('F', ConsoleKey.F, false, false, true));
        Assert.IsTrue(isHandled);
        Assert.IsTrue(menuBar.IsOpened);
        Assert.IsTrue(fileMenuItem.IsOpened);
        Assert.IsFalse(exitFileMenuItem.IsOpened);
        Assert.IsTrue(exitFileMenuItem.IsSelected);
        Assert.IsFalse(aboutMenuItem.IsOpened);
        Assert.IsFalse(aboutAboutMenuItem.IsOpened);

        mockBuffer.Clear();
        menuBar.Draw(mockBuffer);
        content = mockBuffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(content[0].TrimEnd().StartsWith("File About"));
        Assert.IsTrue(content[1].Trim().Contains("╔ File ╗"));
        Assert.IsTrue(content[2].Contains("<Exit>"));
        Assert.IsTrue(content[3].Contains("╚"));

        isHandled = await menuBar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Escape, false, false, false));
        Assert.IsTrue(isHandled);
        Assert.IsFalse(menuBar.IsOpened);
        Assert.IsFalse(fileMenuItem.IsOpened);
        Assert.IsFalse(exitFileMenuItem.IsOpened);
        Assert.IsFalse(exitFileMenuItem.IsSelected);
        Assert.IsFalse(aboutMenuItem.IsOpened);
        Assert.IsFalse(aboutAboutMenuItem.IsOpened);
        mockBuffer.Clear();
        menuBar.Draw(mockBuffer);
        content = mockBuffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(content[0].TrimEnd().StartsWith("File About"));
        #endregion

        #region
        isHandled = await menuBar.HandleKeyEventAsync(new KeyEvent('A', ConsoleKey.A, false, false, true));
        Assert.IsTrue(isHandled);
        Assert.IsTrue(menuBar.IsOpened);
        Assert.IsFalse(fileMenuItem.IsOpened);
        Assert.IsFalse(exitFileMenuItem.IsOpened);
        Assert.IsFalse(exitFileMenuItem.IsSelected);
        Assert.IsTrue(aboutMenuItem.IsOpened);
        Assert.IsFalse(aboutAboutMenuItem.IsOpened);
        menuBar.Draw(mockBuffer);
        content = mockBuffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(content[0].TrimEnd().StartsWith("File About"));
        Assert.IsTrue(content[1].Contains("About"));
        #endregion
    }
}

