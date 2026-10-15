using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls.Buttons;
using iConsole.Commands;
using iConsole.Services;
using iConsole.Test.Core;
using iConsole.Data;

namespace iConsole.Controls.Buttons.Test;

[TestClass]
public class ButtonTest
{
    [TestMethod]
    public async Task TriggerHotKeyAsync_ShouldInvokeCommand()
    {
        var mockCmd = new Mock<ICommand>();
        mockCmd.SetupGet(c => c.Title).Returns("T");
        mockCmd.Setup(c => c.CanExecute()).Returns(true);
        mockCmd.Setup(c => c.RunAsync(null)).ReturnsAsync(true).Verifiable();

        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var btn = new Button("Title", mockCmd.Object, mockTheme.Object)
        {
            ViewSize = new Size(12, 1)
        };

        await btn.TriggerHotKeyAsync();

        mockCmd.Verify(c => c.RunAsync(null), Times.Once);
    }

    [TestMethod]
    public void Draw_ShouldRenderTitle()
    {
        var mockCmd = new Mock<ICommand>();
        mockCmd.SetupGet(c => c.Title).Returns("Hi");
        mockCmd.Setup(c => c.CanExecute()).Returns(true);

        var mockTheme = new Mock<IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var btn = new Button("Hi", mockCmd.Object, mockTheme.Object)
        {
            ViewSize = new Size(10, 1),
            Position = new Position(0, 0)
        };

        var buffer = new MockBuffer(new Size(10, 1));
        btn.Draw(buffer);

        Assert.IsTrue(buffer.ContentAsString.Contains("Hi"));
    }

    [TestMethod]
    public void AnExplicitTitleWinsOverTheCommandsTitle()
    {
        // The hot key bar names its buttons "F1-Help", "F2-Terminal", ...; the commands' own
        // "Help", "Terminal", ... used to replace them, so the bar never said which key did what.
        var command = new Mock<ICommand>();
        command.SetupGet(c => c.Title).Returns("Help");

        var button = new Button("F1-Help", command.Object, new TestThemeService());

        Assert.AreEqual("F1-Help", button.Title);
    }

    [TestMethod]
    public void WithoutATitleTheButtonFollowsItsCommand()
    {
        // What lets a calculator key show the numeral base it selects, e.g. "[HEX]".
        var title = "HEX";
        var command = new Mock<ICommand>();
        command.SetupGet(c => c.Title).Returns(() => title);

        var button = new Button(command.Object, new TestThemeService());
        title = "[HEX]";

        Assert.AreEqual("[HEX]", button.Title);
    }
}
