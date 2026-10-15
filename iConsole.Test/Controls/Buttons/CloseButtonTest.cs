using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Controls.Buttons.Test;

[TestClass]
public class CloseButtonTest
{
    [TestMethod]
    public void Constructor_ShouldSetHotKey()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var btn = new CloseButton(mockTheme.Object);

        Assert.AreEqual('l', btn.HotKeyChar);
    }

    [TestMethod]
    public void Draw_RendersTitle()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(11, 1));
        var btn = new CloseButton(mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(11, 1)
        };

        btn.Draw(buffer);

        // Title comes from CloseParentCommand: "Close Parent"
        Assert.IsTrue(buffer.ContentAsString.Contains("Close"));
    }

    [TestMethod]
    public void Draw_RendersBrackets()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(11, 1));
        var btn = new CloseButton(mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(11, 1)
        };

        btn.Draw(buffer);

        var content = buffer.ContentAsString;
        Assert.IsTrue(content.Contains("["));
        Assert.IsTrue(content.Contains("]"));
    }

    [TestMethod]
    public void Draw_ExactOutput()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(11, 1));
        var btn = new CloseButton(mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(11, 1)
        };

        btn.Draw(buffer);

        // The button's own title "Close" wins over CloseParentCommand.Title ("Close Parent"), which
        // used to be shown cut to "[Close Par]". "Close" centred in the 9 inner columns.
        Assert.AreEqual("[  Close  ]" + Environment.NewLine, buffer.ContentAsString);
    }
}

