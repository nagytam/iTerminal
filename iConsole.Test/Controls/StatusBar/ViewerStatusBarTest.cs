using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class ViewerStatusBarTest
{
    private static ViewerStatusBar CreateViewerStatusBar()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollableModel = new ScrollableModel();
        var searchModel = new SearchModel();
        var textModel = new TextModel();
        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeService = new Mock<IDateTimeProviderService>();
        mockDateTimeService.Setup(m => m.Now).Returns(DateTime.Now);

        return new ViewerStatusBar(
            scrollableModel,
            searchModel,
            textModel,
            mockBeepService.Object,
            mockDateTimeService.Object,
            mockThemeService.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var statusBar = CreateViewerStatusBar();

        Assert.IsNotNull(statusBar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsName()
    {
        var statusBar = CreateViewerStatusBar();

        Assert.AreEqual("ViewerStatusBar", statusBar.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_HasFixedHeight()
    {
        var statusBar = CreateViewerStatusBar();

        Assert.AreEqual(1, statusBar.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_HasChildControls()
    {
        var statusBar = CreateViewerStatusBar();

        Assert.IsTrue(statusBar.Controls.Count > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ViewerStatusBar_IsInstanceOfStatusBar()
    {
        var statusBar = CreateViewerStatusBar();

        Assert.IsInstanceOfType(statusBar, typeof(global::iConsole.Controls.StatusBar.StatusBar));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_DoesNotThrow()
    {
        var statusBar = CreateViewerStatusBar();
        statusBar.Position = new Position(0, 0);
        statusBar.ViewSize = new Size(80, 1);
        var buffer = new MockBuffer(new Size(80, 1));

        statusBar.Draw(buffer);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_CannotBeFocused()
    {
        var statusBar = CreateViewerStatusBar();

        Assert.IsFalse(statusBar.CanFocus);
    }
}
