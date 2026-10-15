using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Jobs;
using iConsole.Services;

namespace iConsole.Jobs.Test;

[TestClass]
public class KeyboardEventConsumerJobTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static KeyEvent F1() => new('\0', ConsoleKey.F1, false, false, false);

    private static (KeyboardEventConsumerJob job, Mock<IBeepService> beep, HelpDialog helpDialog) CreateJob()
    {
        var beep = new Mock<IBeepService>();
        var helpDialog = new HelpDialog(CreateThemeService());
        var job = new KeyboardEventConsumerJob(beep.Object, helpDialog);
        return (job, beep, helpDialog);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_F1UnhandledByModalControl_OpensHelpDialog()
    {
        var (job, beep, helpDialog) = CreateJob();
        var modalControl = new Mock<IControl>();
        modalControl.Setup(c => c.HandleKeyEventAsync(It.IsAny<KeyEvent>())).ReturnsAsync(false);
        var desktop = new Mock<IDesktop>();
        desktop.Setup(d => d.GetModalChildControl()).Returns(modalControl.Object);
        job.OnInitialize(desktop.Object);

        var isHandled = await job.HandleKeyEventAsync(F1());

        Assert.IsTrue(isHandled);
        modalControl.Verify(c => c.ShowDialogAsync(helpDialog, null), Times.Once);
        Assert.AreSame(modalControl.Object, helpDialog.ContextControl);
        beep.Verify(b => b.Beep(), Times.Never);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_KeyHandledByModalControl_DoesNotOpenHelpDialog()
    {
        var (job, beep, helpDialog) = CreateJob();
        var modalControl = new Mock<IControl>();
        modalControl.Setup(c => c.HandleKeyEventAsync(It.IsAny<KeyEvent>())).ReturnsAsync(true);
        var desktop = new Mock<IDesktop>();
        desktop.Setup(d => d.GetModalChildControl()).Returns(modalControl.Object);
        job.OnInitialize(desktop.Object);

        var isHandled = await job.HandleKeyEventAsync(F1());

        Assert.IsTrue(isHandled);
        modalControl.Verify(c => c.ShowDialogAsync(It.IsAny<Dialog>(), It.IsAny<IControl?>()), Times.Never);
        beep.Verify(b => b.Beep(), Times.Never);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_NonF1UnhandledKey_StillBeeps()
    {
        var (job, beep, helpDialog) = CreateJob();
        var modalControl = new Mock<IControl>();
        modalControl.Setup(c => c.HandleKeyEventAsync(It.IsAny<KeyEvent>())).ReturnsAsync(false);
        var desktop = new Mock<IDesktop>();
        desktop.Setup(d => d.GetModalChildControl()).Returns(modalControl.Object);
        job.OnInitialize(desktop.Object);

        var isHandled = await job.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, false, false, false));

        Assert.IsFalse(isHandled);
        modalControl.Verify(c => c.ShowDialogAsync(It.IsAny<Dialog>(), It.IsAny<IControl?>()), Times.Never);
        beep.Verify(b => b.Beep(), Times.Once);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_F1WhileHelpDialogIsAlreadyModal_DoesNotReopen()
    {
        var (job, beep, helpDialog) = CreateJob();
        var desktop = new Mock<IDesktop>();
        desktop.Setup(d => d.GetModalChildControl()).Returns(helpDialog);
        job.OnInitialize(desktop.Object);

        var isHandled = await job.HandleKeyEventAsync(F1());

        Assert.IsFalse(isHandled);
        beep.Verify(b => b.Beep(), Times.Once);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_ModifiedF1_DoesNotOpenHelpDialog()
    {
        var (job, beep, helpDialog) = CreateJob();
        var modalControl = new Mock<IControl>();
        modalControl.Setup(c => c.HandleKeyEventAsync(It.IsAny<KeyEvent>())).ReturnsAsync(false);
        var desktop = new Mock<IDesktop>();
        desktop.Setup(d => d.GetModalChildControl()).Returns(modalControl.Object);
        job.OnInitialize(desktop.Object);

        var isHandled = await job.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F1, false, false, true));

        Assert.IsFalse(isHandled);
        modalControl.Verify(c => c.ShowDialogAsync(It.IsAny<Dialog>(), It.IsAny<IControl?>()), Times.Never);
        beep.Verify(b => b.Beep(), Times.Once);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_NoModalControl_ReturnsFalseWithoutBeeping()
    {
        var (job, beep, helpDialog) = CreateJob();
        var desktop = new Mock<IDesktop>();
        desktop.Setup(d => d.GetModalChildControl()).Returns((IControl?)null);
        job.OnInitialize(desktop.Object);

        var isHandled = await job.HandleKeyEventAsync(F1());

        Assert.IsFalse(isHandled);
        beep.Verify(b => b.Beep(), Times.Never);
    }
}
