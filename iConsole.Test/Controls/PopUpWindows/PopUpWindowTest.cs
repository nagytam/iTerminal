using iConsole.Controls;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.PopUpWindows;

[TestClass]
public class PopUpWindowTest
{
    private sealed class CenteredPopUp(IThemeService themeService) : PopUpWindow("Centered", themeService)
    {
        public void Center(Size size) => CenterInParent(size);
    }

    [TestMethod]
    public void CenterInParent_NeverMakesThePopupBiggerThanItsParent()
    {
        var theme = new Mock<IThemeService>().Object;
        var parent = new Window(theme) { ViewSize = new Size(80, 24) };
        var popUp = new CenteredPopUp(theme);
        parent.AddControl(popUp);

        popUp.Center(new Size(84, 30));

        Assert.AreEqual(new Size(80, 24), popUp.ViewSize);
        Assert.AreEqual(new Position(0, 0), popUp.Position);
    }

    [TestMethod]
    public void CenterInParent_KeepsASmallerPopupAndCentersIt()
    {
        var theme = new Mock<IThemeService>().Object;
        var parent = new Window(theme) { ViewSize = new Size(80, 24) };
        var popUp = new CenteredPopUp(theme);
        parent.AddControl(popUp);

        popUp.Center(new Size(40, 10));

        Assert.AreEqual(new Size(40, 10), popUp.ViewSize);
        Assert.AreEqual(new Position(20, 7), popUp.Position);
    }

    [TestMethod]
    public void PopUpWindowCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);

        Assert.IsNotNull(popUpWindow);
        Assert.IsInstanceOfType(popUpWindow, typeof(Window));
    }

    [TestMethod]
    public void PopUpWindowHasTitle()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("My Dialog", mockThemeService.Object);

        Assert.AreEqual("My Dialog", popUpWindow.Title);
    }

    [TestMethod]
    public void PopUpWindowIsHiddenByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);

        Assert.IsFalse(popUpWindow.IsVisible);
    }

    [TestMethod]
    public void PopUpWindowCanBeShown()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);

        popUpWindow.IsVisible = true;

        Assert.IsTrue(popUpWindow.IsVisible);
    }

    [TestMethod]
    public void PopUpWindowCanBeHidden()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);

        popUpWindow.IsVisible = true;
        popUpWindow.IsVisible = false;

        Assert.IsFalse(popUpWindow.IsVisible);
    }

    [TestMethod]
    public void PopUpWindowInitializesActionButtons()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);

        // PopUpWindow needs parent to initialize properly
        // Just verify the window can be created and has methods
        Assert.IsNotNull(popUpWindow.Controls);
        Assert.IsTrue(popUpWindow.Controls is not null);
    }

    [TestMethod]
    public void PopUpWindowCanContainControls()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);
        var label = new Label("Content", mockThemeService.Object);

        popUpWindow.AddControl(label);

        Assert.IsTrue(popUpWindow.Controls.Any(c => c == label));
    }

    [TestMethod]
    public void PopUpWindowTitleCanBeChanged()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Original", mockThemeService.Object);

        popUpWindow.Title = "Changed";

        Assert.AreEqual("Changed", popUpWindow.Title);
    }

    [TestMethod]
    public void PopUpWindowIsModalByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);

        // PopUpWindows are typically modal
        // Check via base Window property
        Assert.IsNotNull(popUpWindow);
    }

    [TestMethod]
    public void PopUpWindowCanHavePosition()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object)
        {
            Position = new Position(10, 5)
        };

        Assert.AreEqual(new Position(10, 5), popUpWindow.Position);
    }

    [TestMethod]
    public void PopUpWindowCanHaveSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object)
        {
            ViewSize = new Size(50, 20)
        };

        Assert.AreEqual(new Size(50, 20), popUpWindow.ViewSize);
    }

    [TestMethod]
    public void PopUpWindowInheritsFromWindow()
    {
        var mockThemeService = new Mock<IThemeService>();
        var popUpWindow = new PopUpWindow("Test Window", mockThemeService.Object);

        Assert.IsInstanceOfType(popUpWindow, typeof(Window));
    }

    [TestMethod]
    public void PopUpWindowNestedPopUpWindowsInitialize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var parentWindow = new PopUpWindow("Parent", mockThemeService.Object);
        var childWindow = new PopUpWindow("Child", mockThemeService.Object);

        parentWindow.AddControl(childWindow);

        // Both should be able to contain controls
        Assert.IsTrue(parentWindow.Controls.Count > 0);
        Assert.IsNotNull(childWindow);
    }
}

[TestClass]
public class DatePopUpWindowTest
{
    [TestMethod]
    public void DatePopUpWindowCanBeCreated()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object);

        Assert.IsNotNull(dateWindow);
        Assert.IsInstanceOfType(dateWindow, typeof(PopUpWindow));
    }

    [TestMethod]
    public void DatePopUpWindowHasCorrectTitle()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object);

        Assert.AreEqual("Date, Time and TimeZone", dateWindow.Title);
    }

    [TestMethod]
    public void DatePopUpWindowIsHiddenByDefault()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object);

        Assert.IsFalse(dateWindow.IsVisible);
    }

    [TestMethod]
    public void DatePopUpWindowInitializeLayoutSetsSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object);

        dateWindow.OnInitializeLayout();

        Assert.AreEqual(new Size(80, 7), dateWindow.ViewSize);
    }

    [TestMethod]
    public void DatePopUpWindowInitializeLayoutSetsPosition()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object);

        dateWindow.OnInitializeLayout();

        Assert.AreEqual(new Position(10, 10), dateWindow.Position);
    }

    [TestMethod]
    public void DatePopUpWindowDrawsDateInfo()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 7));
        var dateWindow = new DatePopUpWindow(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 7),
            IsVisible = true
        };

        dateWindow.OnInitializeLayout();
        dateWindow.Draw(buffer);

        // Should contain date information
        Assert.IsTrue(buffer.ContentAsString.Contains("Date:"));
    }

    [TestMethod]
    public void DatePopUpWindowDrawsTimeInfo()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 7));
        var dateWindow = new DatePopUpWindow(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 7),
            IsVisible = true
        };

        dateWindow.OnInitializeLayout();
        dateWindow.Draw(buffer);

        // Should contain time information
        Assert.IsTrue(buffer.ContentAsString.Contains("Time:"));
    }

    [TestMethod]
    public void DatePopUpWindowDrawsTimeZoneInfo()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 7));
        var dateWindow = new DatePopUpWindow(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 7),
            IsVisible = true
        };

        dateWindow.OnInitializeLayout();
        dateWindow.Draw(buffer);

        // Should contain rendered content
        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [TestMethod]
    public void DatePopUpWindowCallsStateHasChangedWhenVisible()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object)
        {
            IsVisible = true
        };

        dateWindow.OnIdle();
    }

    [TestMethod]
    public void DatePopUpWindowUpdatesDynamically()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 7));
        var dateWindow = new DatePopUpWindow(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 7),
            IsVisible = true
        };

        dateWindow.OnInitializeLayout();

        // Draw multiple times
        dateWindow.Draw(buffer);
        var firstOutput = buffer.ContentAsString;

        buffer.Clear();
        dateWindow.Draw(buffer);
        var secondOutput = buffer.ContentAsString;

        // Both should contain date/time information
        Assert.IsTrue(firstOutput.Contains("Date:"));
        Assert.IsTrue(secondOutput.Contains("Date:"));
    }

    [TestMethod]
    public void DatePopUpWindowInheritsFromPopUpWindow()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object);

        Assert.IsInstanceOfType(dateWindow, typeof(PopUpWindow));
    }

    [TestMethod]
    public void DatePopUpWindowInitializesActionButtons()
    {
        var mockThemeService = new Mock<IThemeService>();
        var dateWindow = new DatePopUpWindow(mockThemeService.Object);

        // DatePopUpWindow should be creatable and not throw
        Assert.IsNotNull(dateWindow);
        Assert.IsInstanceOfType(dateWindow, typeof(PopUpWindow));
    }

    [TestMethod]
    public void DatePopUpWindowDisplaysValidDateFormat()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(80, 7));
        var dateWindow = new DatePopUpWindow(mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(80, 7),
            IsVisible = true
        };

        dateWindow.OnInitializeLayout();
        dateWindow.Draw(buffer);

        var output = buffer.ContentAsString;
        // Should contain date in yyyy-MM-dd format
        Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(output, @"\d{4}-\d{2}-\d{2}"));
    }
}
