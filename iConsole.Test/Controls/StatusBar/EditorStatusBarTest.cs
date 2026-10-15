using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using iConsole.Controls;
using Moq;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class EditorStatusBarTest
{
    private static EditorStatusBar CreateEditorStatusBar()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockReadOnly = new Mock<IReadOnly>();
        var mockDirtyModel = new Mock<IDirtyModel>();
        var mockDirtyReadOnlyModel = new Mock<IDirtyReadOnlyModel>();
        var scrollableModel = new ScrollableModel();
        var searchModel = new SearchModel();
        var textModel = new TextModel();
        var mockContentModelService = new Mock<IContentModelService>();
        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeService = new Mock<IDateTimeProviderService>();
        mockDateTimeService.Setup(m => m.Now).Returns(DateTime.Now);

        return new EditorStatusBar(
            mockReadOnly.Object,
            mockDirtyModel.Object,
            mockDirtyReadOnlyModel.Object,
            scrollableModel,
            searchModel,
            textModel,
            mockContentModelService.Object,
            mockBeepService.Object,
            mockDateTimeService.Object,
            mockThemeService.Object);
    }

    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var statusBar = CreateEditorStatusBar();

        Assert.IsNotNull(statusBar);
    }

    [TestMethod]
    public void Constructor_SetsName()
    {
        var statusBar = CreateEditorStatusBar();

        Assert.AreEqual("EditorStatusBar", statusBar.Name);
    }

    [TestMethod]
    public void Constructor_HasFixedHeight()
    {
        var statusBar = CreateEditorStatusBar();

        Assert.AreEqual(1, statusBar.ViewSize.Height);
    }

    [TestMethod]
    public void Constructor_HasChildControls()
    {
        var statusBar = CreateEditorStatusBar();

        Assert.IsTrue(statusBar.Controls.Count > 0);
    }

    [TestMethod]
    public void EditorStatusBar_IsInstanceOfStatusBar()
    {
        var statusBar = CreateEditorStatusBar();

        Assert.IsInstanceOfType(statusBar, typeof(global::iConsole.Controls.StatusBar.StatusBar));
    }

    [TestMethod]
    public void Draw_DoesNotThrow()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockReadOnly = new Mock<IReadOnly>();
        var mockDirtyModel = new Mock<IDirtyModel>();
        var mockDirtyReadOnlyModel = new Mock<IDirtyReadOnlyModel>();
        var scrollableModel = new ScrollableModel();
        var searchModel = new SearchModel();
        var textModel = new TextModel();
        var mockContentModelService = new Mock<IContentModelService>();
        var mockBeepService = new Mock<IBeepService>();
        var mockDateTimeService = new Mock<IDateTimeProviderService>();
        mockDateTimeService.Setup(m => m.Now).Returns(DateTime.Now);

        var statusBar = new EditorStatusBar(
            mockReadOnly.Object,
            mockDirtyModel.Object,
            mockDirtyReadOnlyModel.Object,
            scrollableModel,
            searchModel,
            textModel,
            mockContentModelService.Object,
            mockBeepService.Object,
            mockDateTimeService.Object,
            mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 1)
        };
        var buffer = new MockBuffer(new Size(80, 1));

        statusBar.Draw(buffer);
    }

    [TestMethod]
    public void Constructor_CannotBeFocused()
    {
        var statusBar = CreateEditorStatusBar();

        Assert.IsFalse(statusBar.CanFocus);
    }
}
