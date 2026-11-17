using iConsole.Data;
using iConsole.Services;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// A file key with nothing to act on (the cursor on "..", or on an empty folder with nothing marked) says why it did nothing,
/// instead of leaving the screen unchanged.
/// </summary>
[TestClass]
public class FileKeyRefusalTest
{
    private static IContentModelService NoPanelWithAnEntry()
    {
        // No focused model and no last model: nothing is actionable, so every file key is refused.
        return Mock.Of<IContentModelService>();
    }

    private static KeyEvent Press(ConsoleKey key, bool shift = false) =>
        new('\0', key, shift, alt: false, control: false);

    private static void VerifyNoticeShown(Mock<INotificationService> notifications)
    {
        notifications.Verify(
            n => n.Notify("Nothing selected", "Put the cursor on a file or folder, or mark some, first.", NotificationSeverity.Info, null),
            Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F5WithNothingToCopy_SaysWhyItDidNothing()
    {
        var notifications = new Mock<INotificationService>();
        var command = new CopyDialogCommand(null!, NoPanelWithAnEntry(), notifications.Object);

        bool handled = await command.HandleKeyEventAsync(Press(ConsoleKey.F5));

        Assert.IsFalse(handled);
        VerifyNoticeShown(notifications);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F8WithNothingToDelete_SaysWhyItDidNothing()
    {
        var notifications = new Mock<INotificationService>();
        var command = new DeleteDialogCommand(null!, NoPanelWithAnEntry(), notifications.Object);

        bool handled = await command.HandleKeyEventAsync(Press(ConsoleKey.F8));

        Assert.IsFalse(handled);
        VerifyNoticeShown(notifications);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F6WithNothingToRename_SaysWhyItDidNothing()
    {
        var notifications = new Mock<INotificationService>();
        var command = new RenameDialogCommand(null!, NoPanelWithAnEntry(), notifications.Object);

        bool handled = await command.HandleKeyEventAsync(Press(ConsoleKey.F6));

        Assert.IsFalse(handled);
        VerifyNoticeShown(notifications);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftF6WithNothingToMove_SaysWhyItDidNothing()
    {
        var notifications = new Mock<INotificationService>();
        var command = new MoveDialogCommand(null!, NoPanelWithAnEntry(), notifications.Object);

        bool handled = await command.HandleKeyEventAsync(Press(ConsoleKey.F6, shift: true));

        Assert.IsFalse(handled);
        VerifyNoticeShown(notifications);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AKeyThatIsNotTheCommandsOwn_SaysNothing()
    {
        var notifications = new Mock<INotificationService>();
        var command = new CopyDialogCommand(null!, NoPanelWithAnEntry(), notifications.Object);

        await command.HandleKeyEventAsync(Press(ConsoleKey.F9));

        notifications.Verify(
            n => n.Notify(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<NotificationSeverity>(), It.IsAny<TimeSpan?>()),
            Times.Never);
    }
}
