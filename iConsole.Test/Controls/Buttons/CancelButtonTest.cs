using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Controls.Buttons.Test;

[TestClass]
public class CancelButtonTest
{
    [TestMethod]
    public void Constructor_ShouldSetHotKey()
    {
        var cts = new System.Threading.CancellationTokenSource();
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var btn = new CancelButton(cts, mockTheme.Object);

        Assert.AreEqual('c', btn.HotKeyChar);
    }

    [TestMethod]
    public void Draw_RendersTitle()
    {
        var cts = new System.Threading.CancellationTokenSource();
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var btn = new CancelButton(cts, mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(12, 1)
        };

        btn.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Cancel"));
    }

    [TestMethod]
    public void Draw_RendersBrackets()
    {
        var cts = new System.Threading.CancellationTokenSource();
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var btn = new CancelButton(cts, mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(12, 1)
        };

        btn.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("["));
        Assert.IsTrue(content.Contains("]"));
    }
}

