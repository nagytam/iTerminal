using iConsole.Controls;
using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class ReadOnlyIndicatorTest
{
    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockReadOnly = new Mock<IReadOnly>();

        var indicator = new ReadOnlyIndicator(mockReadOnly.Object, mockThemeService.Object);

        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    public void Constructor_SetsDefaultViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockReadOnly = new Mock<IReadOnly>();

        var indicator = new ReadOnlyIndicator(mockReadOnly.Object, mockThemeService.Object);

        Assert.AreEqual(10, indicator.ViewSize.Width);
        Assert.AreEqual(1, indicator.ViewSize.Height);
    }

    [TestMethod]
    public void Draw_ShowsReadOnlyWhenIsReadOnlyTrue()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockReadOnly = new Mock<IReadOnly>();
        mockReadOnly.Setup(m => m.IsReadOnly).Returns(true);
        var buffer = new MockBuffer(new Size(20, 1));
        var indicator = new ReadOnlyIndicator(mockReadOnly.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Read-Only"));
    }

    [TestMethod]
    public void Draw_ShowsWritableWhenIsReadOnlyFalse()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockReadOnly = new Mock<IReadOnly>();
        mockReadOnly.Setup(m => m.IsReadOnly).Returns(false);
        var buffer = new MockBuffer(new Size(20, 1));
        var indicator = new ReadOnlyIndicator(mockReadOnly.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Writable"));
    }

    [TestMethod]
    public void PropertyChanged_OnReadOnly_TriggersStateChange()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockReadOnly = new Mock<IReadOnly>();
        var indicator = new ReadOnlyIndicator(mockReadOnly.Object, mockThemeService.Object);
        indicator.HasStateChanged = false;

        mockReadOnly.Raise(m => m.PropertyChanged += null, new System.ComponentModel.PropertyChangedEventArgs("IsReadOnly"));

        Assert.IsTrue(indicator.HasStateChanged);
    }

    [TestMethod]
    public void Indicator_IsInstanceOfStatusBarItem()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockReadOnly = new Mock<IReadOnly>();

        var indicator = new ReadOnlyIndicator(mockReadOnly.Object, mockThemeService.Object);

        Assert.IsInstanceOfType(indicator, typeof(StatusBarItem));
    }
}
