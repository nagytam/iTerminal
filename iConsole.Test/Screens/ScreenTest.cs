using iConsole.Display;
using iConsole.Drivers;
using iConsole.Screens;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Screens;

[TestClass]
public class ScreenTest
{
    [TestMethod]
    public void Ctor_InitializesConsoleDriverAndDefaultState()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        consoleDriver.Setup(d => d.Clear());
        consoleDriver.SetupSet(d => d.IsCursorVisible = false);
        consoleDriver.Setup(d => d.SetCursorPosition(0, 0));
        consoleDriver.SetupSet(d => d.Title = It.IsAny<string>());

        var themeService = new Mock<IThemeService>(MockBehavior.Strict);

        var screen = new Screen(consoleDriver.Object, themeService.Object);

        Assert.AreEqual(true, screen.IsModal);
        Assert.AreEqual(true, screen.CanFocus);

        consoleDriver.Verify(d => d.Clear(), Times.Once);
        consoleDriver.VerifySet(d => d.IsCursorVisible = false, Times.Once);
        consoleDriver.Verify(d => d.SetCursorPosition(0, 0), Times.Once);
        consoleDriver.VerifySet(d => d.Title = "iConsole", Times.Once);
    }

    [TestMethod]
    public void Title_Setter_ForwardsToConsoleDriver()
    {
        var consoleDriver = new Mock<IConsoleDriver>(MockBehavior.Strict);
        consoleDriver.Setup(d => d.Clear());
        consoleDriver.SetupSet(d => d.IsCursorVisible = false);
        consoleDriver.Setup(d => d.SetCursorPosition(0, 0));
        consoleDriver.SetupSet(d => d.Title = It.IsAny<string>());

        var themeService = new Mock<IThemeService>(MockBehavior.Strict);

        var screen = new Screen(consoleDriver.Object, themeService.Object);

        screen.Title = "MyTitle";

        consoleDriver.VerifySet(d => d.Title = "MyTitle", Times.Once);
    }
}
