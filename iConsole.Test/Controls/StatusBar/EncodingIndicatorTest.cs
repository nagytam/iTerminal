using iConsole.Controls.StatusBar;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;
using System.Text;

namespace iConsole.Test.Controls.StatusBar;

[TestClass]
public class EncodingIndicatorTest
{
    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var mockThemeService = new Mock<IThemeService>();
        var textModel = new TextModel();

        var indicator = new EncodingIndicator(textModel, mockThemeService.Object);

        Assert.IsNotNull(indicator);
    }

    [TestMethod]
    public void Constructor_SetsDefaultViewSize()
    {
        var mockThemeService = new Mock<IThemeService>();
        var textModel = new TextModel();

        var indicator = new EncodingIndicator(textModel, mockThemeService.Object);

        Assert.AreEqual(10, indicator.ViewSize.Width);
        Assert.AreEqual(1, indicator.ViewSize.Height);
    }

    [TestMethod]
    public void Constructor_SetsName()
    {
        var mockThemeService = new Mock<IThemeService>();
        var textModel = new TextModel();

        var indicator = new EncodingIndicator(textModel, mockThemeService.Object);

        Assert.AreEqual("EncodingIndicator", indicator.Name);
    }

    [TestMethod]
    public void Draw_ShowsEncodingName()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var textModel = new TextModel { Encoding = Encoding.UTF8 };
        var buffer = new MockBuffer(new Size(20, 1));
        var indicator = new EncodingIndicator(textModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("UTF-8"));
    }

    [TestMethod]
    public void Draw_ShowsAsciiEncoding()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var textModel = new TextModel { Encoding = Encoding.ASCII };
        var buffer = new MockBuffer(new Size(20, 1));
        var indicator = new EncodingIndicator(textModel, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1)
        };

        indicator.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.ToUpper().Contains("ASCII"));
    }

    [TestMethod]
    public void Indicator_IsInstanceOfStatusBarItem()
    {
        var mockThemeService = new Mock<IThemeService>();
        var textModel = new TextModel();

        var indicator = new EncodingIndicator(textModel, mockThemeService.Object);

        Assert.IsInstanceOfType(indicator, typeof(StatusBarItem));
    }
}
