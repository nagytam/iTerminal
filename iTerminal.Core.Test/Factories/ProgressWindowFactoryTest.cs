using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Factories;
using Moq;

namespace iTerminal.Core.Test.Factories;

[TestClass]
public class ProgressWindowFactoryTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_InitializesFactory()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var factory = new ProgressWindowFactory(mockThemeService.Object);

        Assert.IsNotNull(factory);
    }
}

