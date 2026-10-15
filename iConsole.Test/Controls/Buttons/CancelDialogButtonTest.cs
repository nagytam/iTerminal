using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Controls.Buttons.Test;

[TestClass]
public class CancelDialogButtonTest
{
    [TestMethod]
    public void Constructor_ShouldSetHotKey()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var btn = new CancelDialogButton(mockTheme.Object);

        Assert.AreEqual('c', btn.HotKeyChar);
    }

    [TestMethod]
    public void Draw_RendersTitle()
    {
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var btn = new CancelDialogButton(mockTheme.Object)
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
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(12, 1));
        var btn = new CancelDialogButton(mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(12, 1)
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
        var buffer = new MockBuffer(new Size(12, 1));
        var btn = new CancelDialogButton(mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(12, 1)
        };

        btn.Draw(buffer);

        // The button's own title "Cancel" wins over CancelDialogCommand.Title ("Cancel Dialog"),
        // which used to be shown cut to "[Cancel Dia]". "Cancel" centred in the 10 inner columns.
        Assert.AreEqual("[  Cancel  ]" + Environment.NewLine, buffer.ContentAsString);
    }
}

