using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Commands;
using iConsole.Controls.Buttons;

namespace iConsole.Commands.Test;

[TestClass]
public class SpaceCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_ShouldRunFirstCommandOfButton()
    {
        var mockCmd = new Mock<iConsole.Commands.ICommand>();
        mockCmd.Setup(c => c.RunAsync(null)).ReturnsAsync(true).Verifiable();

        var mockTheme = new Moq.Mock<iConsole.Services.IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var button = new Button("X", mockCmd.Object, mockTheme.Object);
        var space = new SpaceCommand();
        space.Control = button;

        await space.ExecuteAsync(null);

        mockCmd.Verify(c => c.RunAsync(null), Times.AtLeastOnce);
    }
}

