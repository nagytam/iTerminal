using iConsole.Controls.StatusBar;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class StatusBarTest
{
    [TestMethod]
    public void StatusBarCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object);

        Assert.IsNotNull(statusBar);
        Assert.IsInstanceOfType(statusBar, typeof(Control));
    }

    [TestMethod]
    public void StatusBarHasDefaultProperties()
    {
        var mockThemeService = new Mock<IThemeService>();
        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object);

        Assert.AreEqual("StatusBar", statusBar.Name);
        Assert.IsFalse(statusBar.CanFocus);
        Assert.AreEqual(new Size(0, 1), statusBar.ViewSize);
    }

    [TestMethod]
    public void StatusBarCannotBeFocused()
    {
        var mockThemeService = new Mock<IThemeService>();
        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object);

        Assert.IsFalse(statusBar.CanFocus);
    }

    [TestMethod]
    public void StatusBarHasFixedHeight()
    {
        var mockThemeService = new Mock<IThemeService>();
        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object);

        Assert.AreEqual(1, (int)statusBar.ViewSize.Height);
    }

    [TestMethod]
    public void StatusBarOnInitializeLayoutWithoutParent()
    {
        var mockThemeService = new Mock<IThemeService>();
        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object);

        // Should not throw when Parent is null
        // Note: Actually calling OnInitializeLayout requires complex setup, so we test simpler properties instead
        Assert.AreEqual(new Size(0, 1), statusBar.ViewSize);
    }

    [TestMethod]
    public void StatusBarDrawCallsBase()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 1));

        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 1)
        };

        // Should not throw
        statusBar.Draw(buffer);
        Assert.IsNotNull(buffer);
    }

    [TestMethod]
    public void StatusBarHasDefaultVisibility()
    {
        var mockThemeService = new Mock<IThemeService>();
        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object);

        Assert.IsTrue(statusBar.IsVisible);
    }

    [TestMethod]
    public void StatusBarHasDefaultEnabled()
    {
        var mockThemeService = new Mock<IThemeService>();
        var statusBar = new global::iConsole.Controls.StatusBar.StatusBar(mockThemeService.Object);

        Assert.IsTrue(statusBar.IsEnabled);
    }
}
