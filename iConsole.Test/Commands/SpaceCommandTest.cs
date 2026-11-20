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

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_StartsThePressOfAnEnabledButton()
    {
        var mockCmd = new Mock<iConsole.Commands.ICommand>();
        mockCmd.Setup(c => c.CanExecute()).Returns(true);
        mockCmd.Setup(c => c.RunAsync(null)).ReturnsAsync(true);

        var mockTheme = new Moq.Mock<iConsole.Services.IThemeService>();
        mockTheme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());

        var button = new Button("X", mockCmd.Object, mockTheme.Object) { Clock = new iConsole.Services.VirtualTimeProvider() };
        var space = new SpaceCommand();
        space.Control = button;

        await space.ExecuteAsync(null);

        Assert.IsTrue(button.IsPressed, "Space shows the same press as a click.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Help_ShowsSpaceWithADescriptionInsteadOfRepeatingItsName()
    {
        var space = new SpaceCommand();

        Assert.AreNotEqual("Space", space.Title, "The help line must not read \"Space  Space\".");
        Assert.AreNotEqual(space.Title, space.Description, "A description that only repeats the title is left out of the help.");
    }
}
