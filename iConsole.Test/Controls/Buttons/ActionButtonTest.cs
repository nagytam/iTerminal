using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls.Buttons;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Controls.Buttons.Test;

[TestClass]
public class ActionButtonTest
{
    [TestMethod]
    public void ActionView_ShouldBeStored()
    {
        var mockCmd = new Mock<ICommand>();
        mockCmd.SetupGet(c => c.Title).Returns("T");
        mockCmd.Setup(c => c.CanExecute()).Returns(true);

        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var ab = new ActionButton("Act", "[X]", mockCmd.Object, mockTheme.Object);

        Assert.AreEqual("[X]", ab.ActionView);
    }

    [TestMethod]
    public void Draw_WritesNothing_ActionButtonsDrawnByParent()
    {
        var mockCmd = new Mock<ICommand>();
        mockCmd.Setup(c => c.CanExecute()).Returns(true);
        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var ab = new ActionButton("Act", "[X]", mockCmd.Object, mockTheme.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        ab.Draw(buffer);

        Assert.AreEqual(string.Empty, buffer.ContentAsString);
    }
}

