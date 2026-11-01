using iConsole.Controls;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Drivers;
using iConsole.Prompts;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Test.Core;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace iConsole.Test.Prompts;

/// <summary>
/// The service shows a dialog on the first screen and returns what the person answered. These tests answer the dialog
/// the service shows, through its key handler, the way a person would.
/// </summary>
[TestClass]
public class PromptServiceTest
{
    private readonly TestThemeService _theme = new();

    private (PromptService Service, Screen Screen) Create()
    {
        var screen = new Screen(NullConsoleDriver.Instance, _theme) { ViewSize = new Size(80, 24) };
        var screens = new ConcurrentList<IScreen>();
        screens.Add(screen);
        var desktop = new Mock<IDesktop>();
        desktop.SetupGet(d => d.Screens).Returns(screens);
        var services = new Mock<IServiceProvider>();
        services.Setup(s => s.GetService(typeof(IDesktop))).Returns(desktop.Object);

        var service = new PromptService(services.Object, Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), _theme);
        return (service, screen);
    }

    /// <summary>Waits until the service has shown a dialog of type <typeparamref name="T"/> on the screen.</summary>
    private static async Task<T> AwaitDialog<T>(Control screen) where T : Control
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var dialog = screen.Controls.AsSnapshot().OfType<T>().LastOrDefault();
            if (dialog is not null)
            {
                return dialog;
            }
            await Task.Delay(10);
        }
        Assert.Fail($"No {typeof(T).Name} was shown.");
        return null!;
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Confirm_ReturnsTheAnswerGivenInTheDialog()
    {
        var (service, screen) = Create();

        var answer = service.ConfirmAsync(new ConfirmationPrompt("Delete?"));
        var dialog = await AwaitDialog<ConfirmationPromptDialog>(screen);
        await dialog.HandleKeyEventAsync(new KeyEvent('y', ConsoleKey.Y, false, false, false));

        Assert.IsTrue(await answer);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Select_ReturnsTheChosenChoice()
    {
        var (service, screen) = Create();

        var answer = service.SelectAsync(new SelectionPrompt<string>("Fruit?", ["Apple", "Banana"]));
        var dialog = await AwaitDialog<SelectionPromptDialog<string>>(screen);
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.DownArrow, false, false, false));
        await dialog.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.Enter, false, false, false));

        Assert.AreEqual("Banana", await answer);
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Cancelling_ReturnsNull_AndTakesTheDialogDown()
    {
        var (service, screen) = Create();

        var answer = service.ConfirmAsync(new ConfirmationPrompt("Delete?"));
        var dialog = await AwaitDialog<ConfirmationPromptDialog>(screen);
        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));

        Assert.IsNull(await answer);
        Assert.IsFalse(screen.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    [Timeout(10_000)]
    public async Task Prompts_QueueRatherThanStack()
    {
        var (service, screen) = Create();

        var first = service.ConfirmAsync(new ConfirmationPrompt("First?"));
        var second = service.ConfirmAsync(new ConfirmationPrompt("Second?"));

        var firstDialog = await AwaitDialog<ConfirmationPromptDialog>(screen);
        Assert.AreEqual(1, screen.Controls.AsSnapshot().OfType<ConfirmationPromptDialog>().Count(), "Only one prompt is on screen at a time.");

        await firstDialog.HandleKeyEventAsync(new KeyEvent('n', ConsoleKey.N, false, false, false));
        Assert.IsFalse(await first);

        var secondDialog = await AwaitDialog<ConfirmationPromptDialog>(screen);
        await secondDialog.HandleKeyEventAsync(new KeyEvent('y', ConsoleKey.Y, false, false, false));
        Assert.IsTrue(await second);
    }
}
