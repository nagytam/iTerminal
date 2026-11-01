using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class DirtyIndicatorTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockDirtyModel = new Mock<IDirtyReadOnlyModel>();

        var indicator = new DirtyIndicator(mockDirtyModel.Object, mockThemeService.Object);

        Assert.IsNotNull(indicator);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsDefaultViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockDirtyModel = new Mock<IDirtyReadOnlyModel>();

        var indicator = new DirtyIndicator(mockDirtyModel.Object, mockThemeService.Object);

        Assert.AreEqual(10, indicator.ViewSize.Width);
        Assert.AreEqual(1, indicator.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsName()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockDirtyModel = new Mock<IDirtyReadOnlyModel>();

        var indicator = new DirtyIndicator(mockDirtyModel.Object, mockThemeService.Object);

        Assert.AreEqual("DirtyIndicator", indicator.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsChangedWhenDirty()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockDirtyModel = new Mock<IDirtyReadOnlyModel>();
        mockDirtyModel.Setup(m => m.IsDirty).Returns(true);
        var buffer = new MockBuffer(new Size(20, 1));
        var indicator = new DirtyIndicator(mockDirtyModel.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Changed"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_ShowsUnchangedWhenNotDirty()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockDirtyModel = new Mock<IDirtyReadOnlyModel>();
        mockDirtyModel.Setup(m => m.IsDirty).Returns(false);
        var buffer = new MockBuffer(new Size(20, 1));
        var indicator = new DirtyIndicator(mockDirtyModel.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Unchanged"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PropertyChanged_OnDirtyModel_TriggersStateChange()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockDirtyModel = new Mock<IDirtyReadOnlyModel>();
        var indicator = new DirtyIndicator(mockDirtyModel.Object, mockThemeService.Object);
        indicator.HasStateChanged = false;

        mockDirtyModel.Raise(m => m.PropertyChanged += null, new System.ComponentModel.PropertyChangedEventArgs("IsDirty"));

        Assert.IsTrue(indicator.HasStateChanged);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Indicator_IsInstanceOfStatusBarItem()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockDirtyModel = new Mock<IDirtyReadOnlyModel>();

        var indicator = new DirtyIndicator(mockDirtyModel.Object, mockThemeService.Object);

        Assert.IsInstanceOfType(indicator, typeof(StatusBarItem));
    }
}
