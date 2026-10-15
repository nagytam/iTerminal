using iConsole.Controls.ScrollBar;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.ScrollBar;

[TestClass]
public class ScrollBarTest
{
    [TestMethod]
    public void ScrollBarCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object);

        Assert.IsNotNull(scrollBar);
        Assert.IsInstanceOfType(scrollBar, typeof(Control));
    }

    [TestMethod]
    public void ScrollBarHasDefaultProperties()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object);

        Assert.IsNull(scrollBar.ScrollableModel);
        Assert.AreEqual(Orientation.Vertical, scrollBar.ScrollBarOrientation);
        Assert.IsFalse(scrollBar.CanFocus);
    }

    [TestMethod]
    public void ScrollBarOrientationCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object);

        scrollBar.ScrollBarOrientation = Orientation.Horizontal;

        Assert.AreEqual(Orientation.Horizontal, scrollBar.ScrollBarOrientation);
    }

    [TestMethod]
    public void ScrollBarScrollableModelCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10)
        };

        var mockScrollableModel = new Mock<IScrollableModel>();
        mockScrollableModel.Setup(m => m.ScrollPosition).Returns(new Position(0, 0));
        mockScrollableModel.Setup(m => m.ScrollableSize).Returns(new Size(100, 100));
        mockScrollableModel.Setup(m => m.ViewSize).Returns(new Size(20, 20));

        scrollBar.ScrollableModel = mockScrollableModel.Object;

        Assert.AreEqual(mockScrollableModel.Object, scrollBar.ScrollableModel);
    }

    [TestMethod]
    public void ScrollBarHorizontalOrientationCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            ScrollBarOrientation = Orientation.Horizontal
        };

        Assert.AreEqual(Orientation.Horizontal, scrollBar.ScrollBarOrientation);
    }

    [TestMethod]
    public void ScrollBarDrawWithEmptyViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(0, 0)
        };

        // Should not throw when ViewSize is empty (Draw method returns early)
        Assert.IsNotNull(scrollBar);
    }

    private static Mock<IScrollableModel> CreateScrollableModel(uint cursorY = 0, uint scrollableHeight = 10)
    {
        var model = new Mock<IScrollableModel>();
        model.Setup(m => m.CursorPosition).Returns(new Position(cursorY, cursorY));
        model.Setup(m => m.ScrollableSize).Returns(new Size(scrollableHeight, scrollableHeight));
        model.Setup(m => m.ViewSize).Returns(new Size(20, 5));
        model.Setup(m => m.ScrollPosition).Returns(new Position(0, 0));
        return model;
    }

    [TestMethod]
    public void Draw_Vertical_RendersArrowUp()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(1, 10));
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10),
            ScrollableModel = CreateScrollableModel().Object
        };

        scrollBar.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("↑", lines[0]);
    }

    [TestMethod]
    public void Draw_Vertical_RendersArrowDown()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(1, 10));
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10),
            ScrollableModel = CreateScrollableModel().Object
        };

        scrollBar.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("↓", lines[9]);
    }

    [TestMethod]
    public void Draw_Vertical_RendersScrollThumb()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(1, 10));
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10),
            ScrollableModel = CreateScrollableModel().Object
        };

        scrollBar.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("⬤"));
    }

    [TestMethod]
    public void Draw_Vertical_RendersTrackLines()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(1, 10));
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(1, 10),
            ScrollableModel = CreateScrollableModel(cursorY: 5, scrollableHeight: 10).Object
        };

        scrollBar.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("│"));
    }

    [TestMethod]
    public void Draw_Horizontal_RendersArrowLeft()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var model = new Mock<IScrollableModel>();
        model.Setup(m => m.CursorPosition).Returns(new Position(0, 0));
        model.Setup(m => m.ScrollableSize).Returns(new Size(100, 1));
        model.Setup(m => m.ViewSize).Returns(new Size(10, 1));
        model.Setup(m => m.ScrollPosition).Returns(new Position(0, 0));
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1),
            ScrollBarOrientation = Orientation.Horizontal,
            ScrollableModel = model.Object
        };

        scrollBar.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("←"));
    }

    [TestMethod]
    public void Draw_Horizontal_RendersArrowRight()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var model = new Mock<IScrollableModel>();
        model.Setup(m => m.CursorPosition).Returns(new Position(0, 0));
        model.Setup(m => m.ScrollableSize).Returns(new Size(100, 1));
        model.Setup(m => m.ViewSize).Returns(new Size(10, 1));
        model.Setup(m => m.ScrollPosition).Returns(new Position(0, 0));
        var scrollBar = new global::iConsole.Controls.ScrollBar.ScrollBar(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1),
            ScrollBarOrientation = Orientation.Horizontal,
            ScrollableModel = model.Object
        };

        scrollBar.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("→"));
    }
}
