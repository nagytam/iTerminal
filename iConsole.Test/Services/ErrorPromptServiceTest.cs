using iConsole.Data;
using iConsole.Controls;
using iConsole.Desktops;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Test.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iConsole.Test.Services;

/// <summary>A background operation's error prompt: it waits for an answer, and when the operation is cancelled it does not stay on screen.</summary>
[TestClass]
public class ErrorPromptServiceTest
{
    private static (ErrorPromptService Service, Control Root) Service()
    {
        var theme = new TestThemeService();
        var root = new Control(theme);
        var screen = new Mock<IScreen>();
        screen.Setup(s => s.ShowDialogAsync(It.IsAny<Dialog>(), It.IsAny<IControl?>()))
            .Callback<Dialog, IControl?>((dialog, _) => root.AddControl(dialog))
            .Returns(Task.CompletedTask);
        var screens = new ConcurrentList<IScreen>();
        screens.Add(screen.Object);
        var desktop = new Mock<IDesktop>();
        desktop.SetupGet(d => d.Screens).Returns(screens);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IDesktop))).Returns(desktop.Object);
        return (new ErrorPromptService(provider.Object, theme), root);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACancelledOperation_TakesItsPromptDownAtTheNextIdlePass()
    {
        var (service, root) = Service();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ConfirmContinueAsync("Copy error", "Failed to copy", cancellation.Token));

        var prompt = root.Controls.AsSnapshot().OfType<ErrorPromptDialog>().Single();
        prompt.OnIdle();

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(prompt), "an abandoned prompt would keep the keyboard");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAnsweredPrompt_IsNotTouchedByTheIdlePass()
    {
        var (service, root) = Service();
        var answer = service.ConfirmContinueAsync("Copy error", "Failed", CancellationToken.None);
        var prompt = root.Controls.AsSnapshot().OfType<ErrorPromptDialog>().Single();

        prompt.OnIdle();

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(prompt), "nothing has been abandoned");
        Assert.IsFalse(answer.IsCompleted);
    }
}
