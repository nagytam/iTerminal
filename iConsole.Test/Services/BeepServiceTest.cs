using iConsole.Services;
using Moq;
using iConsole.Drivers;

namespace iConsole.Test.Services;

[TestClass]
public class BeepServiceTest
{
    [TestMethod]
    public void BeepServiceBeep()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        var beepService = new BeepService(mockConsoleDriver.Object);

        beepService.Beep();

        mockConsoleDriver.Verify(m => m.Beep(), Times.Once);
    }

    [TestMethod]
    public void BeepServiceMultipleBeeps()
    {
        var mockConsoleDriver = new Mock<IConsoleDriver>();
        var beepService = new BeepService(mockConsoleDriver.Object);

        beepService.Beep();
        beepService.Beep();
        beepService.Beep();

        mockConsoleDriver.Verify(m => m.Beep(), Times.Exactly(3));
    }
}
