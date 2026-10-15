using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ColorMapTest
{
    [TestMethod]
    public void Constructor_InitializesWithCorrectSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());

        var colorMap = new ColorMap(mockThemeService.Object);

        Assert.AreEqual(16, colorMap.ViewSize.Width);
        Assert.AreEqual(2, colorMap.ViewSize.Height);
    }

    [TestMethod]
    public void Draw_RendersContent()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(16, 2));
        var colorMap = new ColorMap(mockThemeService.Object)
        {
            Position = new Position(0, 0)
        };

        colorMap.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("*"));
    }

    [TestMethod]
    public void Draw_WithLargerBuffer_RendersCorrectly()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(50, 10));
        var colorMap = new ColorMap(mockThemeService.Object)
        {
            Position = new Position(5, 3)
        };

        colorMap.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Length > 0);
    }

    [TestMethod]
    public void ViewSize_IsCorrect()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var colorMap = new ColorMap(mockThemeService.Object);

        Assert.AreEqual(16, colorMap.ViewSize.Width);
        Assert.AreEqual(2, colorMap.ViewSize.Height);
    }
}
