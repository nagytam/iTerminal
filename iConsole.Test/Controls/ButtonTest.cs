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
    [Timeout(60_000)]
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

    [Timeout(60_000)]
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
        mockCommand.Setup(x => x.RunAsync(It.IsAny<object?>())).ReturnsAsync(true);
        var button = new Button("Delete", mockCommand.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1)
        };
        return (button, mockCommand);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftClickRunsTheCommand()
    {
        var (button, command) = CreateClickableButton();

        await button.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });

        command.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RightClickDoesNotRunTheCommand()
    {
        var (button, command) = CreateClickableButton();

        var handled = await button.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), RightClicked = true });

        Assert.IsFalse(handled);
        command.Verify(x => x.RunAsync(It.IsAny<object?>()), Times.Never);
        Assert.IsFalse(button.IsPressed);
    }

    private static Button CreateTimedButton(out VirtualTimeProvider clock, ColorScheme? scheme = null, bool destructive = false)
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(scheme ?? new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        clock = new VirtualTimeProvider();
        return new Button("Delete", mockCommand.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1),
            Clock = clock,
            IsDestructive = destructive,
        };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LeftClickPressAnimatesThroughPressedBrightenedAndNormalColours()
    {
        var (button, _) = CreateClickableButton();
        var clock = new VirtualTimeProvider();
        button.Clock = clock;
        var screen = new ScreenBuffer(10, 1);
        button.Draw(screen);
        var normalBackground = screen.BackgroundAt(1, 0);

        await button.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });
        button.Draw(screen);
        Assert.IsTrue(button.IsPressed);
        Assert.AreEqual(Color.DarkYellow, screen.BackgroundAt(1, 0), "Step 0 is the pressed colour.");

        clock.Advance(TimeSpan.FromMilliseconds(50));
        button.Draw(screen);
        Assert.AreEqual(Color.Blue, screen.BackgroundAt(1, 0), "Step 1 brightens the normal DarkBlue background to Blue.");

        clock.Advance(TimeSpan.FromMilliseconds(50));
        button.Draw(screen);
        Assert.AreEqual(normalBackground, screen.BackgroundAt(1, 0), "Step 2 is the normal colour.");

        clock.Advance(TimeSpan.FromMilliseconds(50));
        button.OnIdle();
        Assert.IsFalse(button.IsPressed, "The press ends at PressFeedback.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PressStepsOnlyRepaintWhenTheStepChanges()
    {
        var button = CreateTimedButton(out var clock);
        button.StartPress();
        button.HasStateChanged = false;

        button.OnIdle();
        Assert.IsTrue(button.HasStateChanged, "The first idle after a start moves to step 0 and repaints.");

        button.HasStateChanged = false;
        clock.Advance(TimeSpan.FromMilliseconds(10));
        button.OnIdle();
        Assert.IsFalse(button.HasStateChanged, "Idle inside the same step repaints nothing.");

        clock.Advance(TimeSpan.FromMilliseconds(40));
        button.OnIdle();
        Assert.IsTrue(button.HasStateChanged, "Moving to step 1 repaints.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickOnACommandThatDeclinesShowsNoPress()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        mockCommand.Setup(x => x.RunAsync(It.IsAny<object?>())).ReturnsAsync(false);
        var button = new Button("Copy", mockCommand.Object, mockThemeService.Object)
        {
            Position = new Position(0, 0),
            ViewSize = new Size(10, 1),
            Clock = new VirtualTimeProvider(),
        };

        await button.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), LeftClicked = true });

        Assert.IsFalse(button.IsPressed, "A click that the command declines (nothing to copy) shows no press.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DisabledButtonDoesNotPress()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(false);
        var button = new Button("Off", mockCommand.Object, mockThemeService.Object) { Clock = new VirtualTimeProvider() };

        button.StartPress();

        Assert.IsFalse(button.IsPressed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DestructiveButtonKeepsItsErrorColourInTheBrightenedStep()
    {
        var scheme = new ColorScheme { Error = Color.DarkRed };
        var button = CreateTimedButton(out var clock, scheme, destructive: true);
        var screen = new ScreenBuffer(10, 1);
        button.Draw(screen);
        button.StartPress();

        clock.Advance(TimeSpan.FromMilliseconds(50));
        button.Draw(screen);

        Assert.AreEqual(Color.DarkRed, screen.BackgroundAt(1, 0), "A destructive button has no brighter error colour, so it keeps Error.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AKeyTheCommandTakesStartsThePress()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        mockCommand.Setup(x => x.ActiveKey).Returns(new Key(ConsoleKey.F5, false, false, false));
        mockCommand.Setup(x => x.HandleKeyEventAsync(It.IsAny<KeyEvent>())).ReturnsAsync(true);
        var button = new Button("F5-Copy", mockCommand.Object, mockThemeService.Object) { Clock = new VirtualTimeProvider() };

        var handled = await button.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F5, false, false, false));

        Assert.IsTrue(handled);
        Assert.IsTrue(button.IsPressed, "A function key bound to the button's command shows the press.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AKeyTheCommandDeclinesEndsThePressAtOnce()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        mockCommand.Setup(x => x.ActiveKey).Returns(new Key(ConsoleKey.F5, false, false, false));
        mockCommand.Setup(x => x.HandleKeyEventAsync(It.IsAny<KeyEvent>())).ReturnsAsync(false);
        var button = new Button("F5-Copy", mockCommand.Object, mockThemeService.Object) { Clock = new VirtualTimeProvider() };

        var handled = await button.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F5, false, false, false));

        Assert.IsFalse(handled);
        Assert.IsFalse(button.IsPressed, "A declined key shows no press.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnACloseButtonShowsThePress()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var button = new CloseButton(mockThemeService.Object) { Clock = new VirtualTimeProvider() };

        await button.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.IsTrue(button.IsPressed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ActionButtonDoesNotPress()
    {
        var mockThemeService = new Mock<IThemeService>();
        mockThemeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        var mockCommand = new Mock<ICommand>();
        mockCommand.Setup(x => x.CanExecute()).Returns(true);
        var button = new ActionButton("Open", "view", mockCommand.Object, mockThemeService.Object) { Clock = new VirtualTimeProvider() };

        button.StartPress();

        Assert.IsFalse(button.IsPressed, "An action button's press could never be painted, so it does not start.");
    }
}
