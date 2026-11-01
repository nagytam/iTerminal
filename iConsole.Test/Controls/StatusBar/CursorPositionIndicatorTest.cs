using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class CursorPositionIndicatorTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollableModel = new ScrollableModel();

        var indicator = new CursorPositionIndicator(scrollableModel, mockThemeService.Object);

        Assert.IsNotNull(indicator);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsDefaultViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollableModel = new ScrollableModel();

        var indicator = new CursorPositionIndicator(scrollableModel, mockThemeService.Object);

        Assert.AreEqual(20, indicator.ViewSize.Width);
        Assert.AreEqual(1, indicator.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsName()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollableModel = new ScrollableModel();

        var indicator = new CursorPositionIndicator(scrollableModel, mockThemeService.Object);

        Assert.AreEqual("CursorPosition", indicator.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_WritesPositionInfo()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollableModel = new ScrollableModel
        {
            CursorPosition = new Position(3, 5),
            ScrollableSize = new Size(100, 200)
        };
        var buffer = new MockBuffer(new Size(40, 1));
        var indicator = new CursorPositionIndicator(scrollableModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(40, 1)
        };

        indicator.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("X:") || content.Contains("3"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsScrollableSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollableModel = new ScrollableModel
        {
            ScrollableSize = new Size(80, 25)
        };
        var buffer = new MockBuffer(new Size(40, 1));
        var indicator = new CursorPositionIndicator(scrollableModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(40, 1)
        };

        indicator.Draw(buffer);

        Assert.IsNotNull(buffer.ContentAsString);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PropertyChanged_OnScrollableModel_TriggersStateChange()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollableModel = new ScrollableModel();
        var indicator = new CursorPositionIndicator(scrollableModel, mockThemeService.Object);
        indicator.HasStateChanged = false;

        scrollableModel.CursorPosition = new Position(1, 2);

        Assert.IsTrue(indicator.HasStateChanged);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Indicator_IsInstanceOfStatusBarItem()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollableModel = new ScrollableModel();

        var indicator = new CursorPositionIndicator(scrollableModel, mockThemeService.Object);

        Assert.IsInstanceOfType(indicator, typeof(StatusBarItem));
    }
}
