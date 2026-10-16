using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class ButtonTest
{
    [TestMethod]
    public async Task ButtonHandlesKeyEvents()
    {
        var mockThemeService = new Mock<IThemeService>();
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        var button = new Button("Title", mockCommand.Object, mockThemeService.Object)
        {
            IsEnabled = true,
            ViewSize = new Size(25, 1)
        };
        button.SetFocus();
        mockCommand.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Never);
        await button.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        mockCommand.Verify(x => x.CanExecute(), Times.AtLeastOnce);
        mockCommand.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Once);
    }

    [TestMethod]
    public void Draw()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var buffer = new MockBuffer(new Size(10, 1));
        var mockCommand = new Mock<ICommand>();
        var button = new Button("Title", mockCommand.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };

        button.Draw(buffer);
        Assert.AreEqual("[ Title  ]" + Environment.NewLine, buffer.ContentAsString);
    }

    private static (Button Button, Mock<ICommand> Command) CreateClickableButton()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        var button = new Button("Delete", mockCommand.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };
        return (button, mockCommand);
    }

    [TestMethod]
    public async Task LeftClickRunsTheCommand()
    {
        var (button, command) = CreateClickableButton();

        await button.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });

        command.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Once);
    }

    [TestMethod]
    public async Task RightClickDoesNotRunTheCommand()
    {
        var (button, command) = CreateClickableButton();

        var handled = await button.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), RightClicked = true });

        Assert.IsFalse(handled);
        command.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Never);
        Assert.IsFalse(button.IsPressed);
    }

    [TestMethod]
    public async Task LeftClickDrawsTheButtonPressedUntilTheFeedbackHasPassed()
    {
        var (button, _) = CreateClickableButton();
        var screen = new ScreenBuffer(10, 1);

        button.Draw(screen);
        var idleBackground = screen.BackgroundAt(1, 0);
        await button.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });
        button.Draw(screen);

        Assert.IsTrue(button.IsPressed);
        Assert.AreNotEqual(idleBackground, screen.BackgroundAt(1, 0), "A click must be visible on the button.");

        await Task.Delay(Button.PressFeedback + TimeSpan.FromMilliseconds(100));
        button.OnIdle();
        Assert.IsFalse(button.IsPressed);
    }
}
