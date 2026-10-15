using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class WindowTest
{
    [TestMethod]
    public void WindowCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        Assert.IsNotNull(window);
        Assert.IsInstanceOfType(window, typeof(Control));
    }

    [TestMethod]
    public void WindowHasDefaultProperties()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        Assert.AreEqual(new Position(0, 0), window.Position);
        Assert.AreEqual(new Size(0, 0), window.ViewSize);
        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(window.IsEnabled);
        Assert.IsNotNull(window.Controls);
    }

    [TestMethod]
    public void WindowPositionCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);
        var position = new Position(10, 5);

        window.Position = position;

        Assert.AreEqual(position, window.Position);
    }

    [TestMethod]
    public void WindowSizeCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);
        var size = new Size(50, 20);

        window.ViewSize = size;

        Assert.AreEqual(size, window.ViewSize);
    }

    [TestMethod]
    public void WindowIsVisibleByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        Assert.IsTrue(window.IsVisible);
    }

    [TestMethod]
    public void WindowVisibilityCanBeToggled()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        window.IsVisible = false;
        Assert.IsFalse(window.IsVisible);

        window.IsVisible = true;
        Assert.IsTrue(window.IsVisible);
    }

    [TestMethod]
    public void WindowIsEnabledByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        Assert.IsTrue(window.IsEnabled);
    }

    [TestMethod]
    public void WindowCanBeDisabled()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        window.IsEnabled = false;
        Assert.IsFalse(window.IsEnabled);
    }

    [TestMethod]
    public void WindowCanContainChildControls()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var window = new Window(mockThemeService.Object);
        var childControl = new Label("Child", mockThemeService.Object);

        window.Controls.Add(childControl);

        Assert.AreEqual(1, window.Controls.Count);
        Assert.AreEqual(childControl, window.Controls[0]);
    }

    [TestMethod]
    public void WindowCanHaveParent()
    {
        var mockThemeService = new Mock<IThemeService>();
        var parentWindow = new Window(mockThemeService.Object);
        var childWindow = new Window(mockThemeService.Object);

        childWindow.Parent = parentWindow;

        Assert.AreEqual(parentWindow, childWindow.Parent);
    }

    [TestMethod]
    public void WindowZOrderCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        window.ZOrder = 5;

        Assert.AreEqual(5, window.ZOrder);
    }

    [TestMethod]
    public void WindowNameCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);
        var name = "MyWindow";

        window.Name = name;

        Assert.AreEqual(name, window.Name);
    }

    [TestMethod]
    public void WindowBoundRectIsCalculatedCorrectly()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object)
        {
            Position = new Position(5, 10),
            ViewSize = new Size(20, 15)
        };

        var boundRect = window.BoundRect;

        Assert.AreEqual(new Position(5, 10), boundRect.Position);
        Assert.AreEqual(new Size(20, 15), boundRect.Size);
    }

    [TestMethod]
    public void WindowAbsolutePositionIsCalculatedWithoutParent()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object)
        {
            Position = new Position(5, 10)
        };

        Assert.AreEqual(new Position(5, 10), window.AbsolutePosition);
    }

    [TestMethod]
    public void WindowAbsolutePositionIsCalculatedWithParent()
    {
        var mockThemeService = new Mock<IThemeService>();
        var parentWindow = new Window(mockThemeService.Object)
        {
            Position = new Position(10, 5)
        };
        var childWindow = new Window(mockThemeService.Object)
        {
            Position = new Position(3, 2),
            Parent = parentWindow
        };

        // Absolute position = parent's absolute position + child's position
        Assert.AreEqual(new Position(13, 7), childWindow.AbsolutePosition);
    }

    [TestMethod]
    public void WindowCanFocusPropertyDefaultsToFalse()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        // Windows typically cannot focus directly
        Assert.IsFalse(window.CanFocus);
    }

    [TestMethod]
    public void WindowIsModalCanBeSet()
    {
        var mockThemeService = new Mock<IThemeService>();
        var window = new Window(mockThemeService.Object);

        window.IsModal = true;

        Assert.IsTrue(window.IsModal);
    }

    [TestMethod]
    public void Draw_EmptySize_DrawsNothing()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 3));
        var window = new Window(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(0, 0)
        };

        window.Draw(buffer);

        Assert.AreEqual(string.Empty, buffer.ContentAsString);
    }

    [TestMethod]
    public void Draw_WithBorder_DrawsTopRow()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 3));
        var window = new Window(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 3)
        };

        window.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("╔════════╗", lines[0]);
    }

    [TestMethod]
    public void Draw_WithBorder_DrawsSideVerticals()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 3));
        var window = new Window(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 3)
        };

        window.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[1].StartsWith("║"));
        Assert.IsTrue(lines[1].EndsWith("║"));
    }

    [TestMethod]
    public void Draw_WithBorder_DrawsBottomRow()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 3));
        var window = new Window(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 3)
        };

        window.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.AreEqual("╚════════╝", lines[2]);
    }

    [TestMethod]
    public void Draw_WithTitle_ShowsTitleInTopBorder()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 3));
        var window = new Window(mockThemeService.Object)
        {
            Title = "Hello",
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3)
        };

        window.Draw(buffer);

        var lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].Contains("Hello"));
        Assert.IsTrue(lines[0].StartsWith("╔"));
        Assert.IsTrue(lines[0].EndsWith("╗"));
    }

    [TestMethod]
    public void Draw_WithChildLabel_ChildIsRendered()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(20, 5));
        var window = new Window(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 5)
        };
        var label = new Label("Inner", mockThemeService.Object)
        {
            Position = new Position(1, 2),
            ViewSize = new Size(10, 1),
            Parent = window
        };
        window.Controls.Add(label);

        window.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Inner"));
    }
}
