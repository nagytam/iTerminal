using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Drivers;
using iConsole.Layouts;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Layouts;

[TestClass]
public class LayoutTest
{
    private Mock<IThemeService> CreateMockThemeService()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mockThemeService;
    }

    private Window CreateParentWindow()
    {
        var mockThemeService = CreateMockThemeService();
        return new Window(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(100, 50)
        };
    }

    [TestMethod]
    public void Rect_SetsPositionAndSize()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var control = new Label("Test", mockThemeService.Object);
        parent.AddControl(control);

        Layout.Rect(control, new Position(10, 5), new Size(20, 3));

        Assert.AreEqual(10, control.Position.X);
        Assert.AreEqual(5, control.Position.Y);
        Assert.AreEqual(20, control.ViewSize.Width);
        Assert.AreEqual(3, control.ViewSize.Height);
    }

    [TestMethod]
    public void Rect_WithoutParent_ThrowsException()
    {
        var mockThemeService = CreateMockThemeService();
        var control = new Label("Test", mockThemeService.Object);

        try
        {
            Layout.Rect(control, new Position(0, 0), new Size(10, 10));
            Assert.Fail("Expected ArgumentNullException");
        }
        catch (ArgumentNullException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void Position_SetsPosition()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var control = new Label("Test", mockThemeService.Object);
        parent.AddControl(control);

        Layout.Position(control, new Position(15, 10));

        Assert.AreEqual(15, control.Position.X);
        Assert.AreEqual(10, control.Position.Y);
    }

    [TestMethod]
    public void Position_WithoutParent_ThrowsException()
    {
        var mockThemeService = CreateMockThemeService();
        var control = new Label("Test", mockThemeService.Object);

        try
        {
            Layout.Position(control, new Position(0, 0));
            Assert.Fail("Expected ArgumentNullException");
        }
        catch (ArgumentNullException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void LeftBottom_PositionsAtBottomLeft()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var control = new Label("Test", mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };
        parent.AddControl(control);

        Layout.LeftBottom(control);

        Assert.AreEqual(0, control.Position.X);
        Assert.AreEqual(45, control.Position.Y); // 50 - 5
    }

    [TestMethod]
    public void LeftBottom_WithoutParent_ThrowsException()
    {
        var mockThemeService = CreateMockThemeService();
        var control = new Label("Test", mockThemeService.Object);

        try
        {
            Layout.LeftBottom(control);
            Assert.Fail("Expected ArgumentNullException");
        }
        catch (ArgumentNullException)
        {
            // Expected
        }
    }

    [TestMethod]
    public void Fill_WithNoParent_FillsConsoleWindow()
    {
        var mockThemeService = CreateMockThemeService();
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(d => d.WindowSize).Returns(new Size(120, 40));
        var control = new Label("Test", mockThemeService.Object);

        Layout.Fill(mockDriver.Object, control);

        Assert.AreEqual(0, control.Position.X);
        Assert.AreEqual(0, control.Position.Y);
        Assert.AreEqual(120, control.ViewSize.Width);
        Assert.AreEqual(40, control.ViewSize.Height);
    }

    [TestMethod]
    public void Fill_WithParent_DoesNothing()
    {
        var mockThemeService = CreateMockThemeService();
        var mockDriver = new Mock<IConsoleDriver>();
        mockDriver.Setup(d => d.WindowSize).Returns(new Size(120, 40));
        var parent = CreateParentWindow();
        var control = new Label("Test", mockThemeService.Object)
        {
            Position = new Position(10, 10),
            ViewSize = new Size(30, 20)
        };
        parent.AddControl(control);

        Layout.Fill(mockDriver.Object, control);

        Assert.AreEqual(10, control.Position.X);
        Assert.AreEqual(10, control.Position.Y);
        Assert.AreEqual(30, control.ViewSize.Width);
        Assert.AreEqual(20, control.ViewSize.Height);
    }

    [TestMethod]
    public void Left_PositionsLeftOfRelative()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var relativeControl = new Label("Relative", mockThemeService.Object)
        {
            Position = new Position(50, 20),
            ViewSize = new Size(10, 5)
        };
        var control = new Label("Test", mockThemeService.Object)
        {
            ViewSize = new Size(15, 5)
        };
        parent.AddControl(relativeControl);
        parent.AddControl(control);

        Layout.Left(control, relativeControl);

        Assert.AreEqual(35, control.Position.X); // 50 - 15
        Assert.AreEqual(20, control.Position.Y);
    }

    [TestMethod]
    public void Bottom_PositionsBelowRelative()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var relativeControl = new Label("Relative", mockThemeService.Object)
        {
            Position = new Position(10, 10),
            ViewSize = new Size(20, 5)
        };
        var control = new Label("Test", mockThemeService.Object);
        parent.AddControl(relativeControl);
        parent.AddControl(control);

        Layout.Bottom(control, relativeControl);

        Assert.AreEqual(10, control.Position.X);
        Assert.AreEqual(15, control.Position.Y); // 10 + 5
    }

    [TestMethod]
    public void Right_PositionsRightOfRelative()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var relativeControl = new Label("Relative", mockThemeService.Object)
        {
            Position = new Position(10, 10),
            ViewSize = new Size(20, 5)
        };
        var control = new Label("Test", mockThemeService.Object);
        parent.AddControl(relativeControl);
        parent.AddControl(control);

        Layout.Right(control, relativeControl);

        Assert.AreEqual(30, control.Position.X); // 10 + 20
        Assert.AreEqual(10, control.Position.Y);
    }

    [TestMethod]
    public void PadRight_PositionsAtRightEdge()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var control = new Label("Test", mockThemeService.Object)
        {
            ViewSize = new Size(20, 5)
        };
        parent.AddControl(control);

        Layout.PadRight(control);

        Assert.AreEqual(81, control.Position.X); // 100 - 20 + 1
        Assert.AreEqual(0, control.Position.Y);
    }

    [TestMethod]
    public void StackHorizontal_PositionsControlsInRow()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var control1 = new Label("Test1", mockThemeService.Object) { ViewSize = new Size(10, 5) };
        var control2 = new Label("Test2", mockThemeService.Object) { ViewSize = new Size(15, 5) };
        var control3 = new Label("Test3", mockThemeService.Object) { ViewSize = new Size(20, 5) };
        parent.AddControl(control1);
        parent.AddControl(control2);
        parent.AddControl(control3);

        Layout.StackHorizontal(new[] { control1, control2, control3 });

        Assert.AreEqual(0, control1.Position.X);
        Assert.AreEqual(10, control2.Position.X);
        Assert.AreEqual(25, control3.Position.X); // 10 + 15
    }

    [TestMethod]
    public void StackVertical_PositionsControlsInColumn()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var control1 = new Label("Test1", mockThemeService.Object) { ViewSize = new Size(10, 3) };
        var control2 = new Label("Test2", mockThemeService.Object) { ViewSize = new Size(10, 5) };
        var control3 = new Label("Test3", mockThemeService.Object) { ViewSize = new Size(10, 4) };
        parent.AddControl(control1);
        parent.AddControl(control2);
        parent.AddControl(control3);

        Layout.StackVertical(new[] { control1, control2, control3 });

        Assert.AreEqual(0, control1.Position.Y);
        Assert.AreEqual(3, control2.Position.Y);
        Assert.AreEqual(8, control3.Position.Y); // 3 + 5
    }

    [TestMethod]
    public void StackVertical_WithStartPosition_UsesStartCoordinates()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow();
        var control1 = new Label("Test1", mockThemeService.Object) { ViewSize = new Size(10, 3) };
        var control2 = new Label("Test2", mockThemeService.Object) { ViewSize = new Size(10, 5) };
        parent.AddControl(control1);
        parent.AddControl(control2);

        Layout.StackVertical(new[] { control1, control2 }, 10, 5);

        Assert.AreEqual(10, control1.Position.X);
        Assert.AreEqual(5, control1.Position.Y);
        Assert.AreEqual(10, control2.Position.X);
        Assert.AreEqual(8, control2.Position.Y); // 5 + 3
    }

    [TestMethod]
    public void Center_PositionsAtCenterOfParent()
    {
        var mockThemeService = CreateMockThemeService();
        var parent = CreateParentWindow(); // 100x50
        var control = new Label("Test", mockThemeService.Object)
        {
            ViewSize = new Size(20, 10)
        };
        parent.AddControl(control);

        Layout.Center(control);

        Assert.AreEqual(40, control.Position.X); // (100 - 20) / 2
        Assert.AreEqual(20, control.Position.Y); // (50 - 10) / 2
    }

    [TestMethod]
    public void Center_WithoutParent_DoesNothing()
    {
        var mockThemeService = CreateMockThemeService();
        var control = new Label("Test", mockThemeService.Object)
        {
            Position = new Position(5, 5),
            ViewSize = new Size(20, 10)
        };

        Layout.Center(control);

        Assert.AreEqual(5, control.Position.X);
        Assert.AreEqual(5, control.Position.Y);
    }
}
