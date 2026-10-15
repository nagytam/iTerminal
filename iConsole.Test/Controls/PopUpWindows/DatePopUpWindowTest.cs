using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls.PopUpWindows;
using iConsole.Services;
using iConsole.Test.Core;
using iConsole.Data;

namespace iConsole.Controls.PopUpWindows.Test;

[TestClass]
public class DatePopUpWindowTest
{
    [TestMethod]
    public void Draw_ShouldWriteDateLines()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var win = new DatePopUpWindow(mockTheme.Object)
        {
            ViewSize = new Size(80, 7),
            Position = new Position(0, 0)
        };

        var buffer = new MockBuffer(new Size(80, 7));
        win.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Date:"));
        Assert.IsTrue(buffer.ContentAsString.Contains("Time:"));
    }
}

