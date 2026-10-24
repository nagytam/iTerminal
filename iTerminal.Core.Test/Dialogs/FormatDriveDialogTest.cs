using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Drives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Format drive: the drive letter must be typed back before anything is erased, and a failure stays in the dialog in words.</summary>
[TestClass]
public class FormatDriveDialogTest
{
    private static (FormatDriveDialog Dialog, Mock<IDriveActionsService> Drives, Mock<INotificationService> Notifications) Create()
    {
        var drives = new Mock<IDriveActionsService>();
        var notifications = new Mock<INotificationService>();
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.ContentModels).Returns([]);
        var dialog = new FormatDriveDialog(
            drives.Object,
            notifications.Object,
            contentModelService.Object,
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            TestTheme());
        return (dialog, drives, notifications);
    }

    [TestMethod]
    public async Task AWrongLetter_IsRefusedAndNothingIsFormatted()
    {
        var (dialog, drives, _) = Create();
        dialog.Load("D:\\", "DATA", "NTFS");
        await ShowAsync(dialog);
        dialog.Confirmation = "C";

        var formatted = await dialog.FormatAsync();

        Assert.IsFalse(formatted);
        StringAssert.Contains(dialog.Error, "Type D to confirm that D: is to be erased.");
        drives.Verify(d => d.FormatAsync(It.IsAny<string>(), It.IsAny<DriveFormatOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task TheLetterTypedBack_WithAColonOrSlash_IsAccepted()
    {
        var (dialog, drives, notifications) = Create();
        drives.Setup(d => d.FormatAsync("D:\\", It.IsAny<DriveFormatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveActionResult(true, "Formatted D:"));
        dialog.Load("D:\\", "DATA", "NTFS");
        dialog.Confirmation = "d:\\";

        var formatted = await dialog.FormatAsync();

        Assert.IsTrue(formatted);
        notifications.Verify(n => n.Notify("Format", "Formatted D:", NotificationSeverity.Success), Times.Once);
    }

    [TestMethod]
    public async Task AFailedFormat_StaysInTheDialogInWords()
    {
        var (dialog, drives, _) = Create();
        drives.Setup(d => d.FormatAsync(It.IsAny<string>(), It.IsAny<DriveFormatOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DriveActionResult(false, "The volume is in use."));
        dialog.Load("D:\\", "DATA", "NTFS");
        await ShowAsync(dialog);
        dialog.Confirmation = "D";

        var formatted = await dialog.FormatAsync();

        Assert.IsFalse(formatted);
        StringAssert.Contains(dialog.Error, "The volume is in use.");
    }

    [TestMethod]
    public async Task ANonLetteredRoot_IsRefusedInWords()
    {
        var (dialog, drives, _) = Create();
        dialog.Load("/mnt/data", "DATA", "ext4");
        await ShowAsync(dialog);
        dialog.Confirmation = "D";

        Assert.IsFalse(await dialog.FormatAsync());

        StringAssert.Contains(dialog.Error, "Not a lettered drive.");
        drives.Verify(d => d.FormatAsync(It.IsAny<string>(), It.IsAny<DriveFormatOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task AFormatAlreadyRunning_RefusesASecondOneAndSaysSo()
    {
        var (dialog, drives, _) = Create();
        var release = new TaskCompletionSource<DriveActionResult>();
        drives.Setup(d => d.FormatAsync(It.IsAny<string>(), It.IsAny<DriveFormatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        dialog.Load("D:\\", "DATA", "NTFS");
        await ShowAsync(dialog);
        dialog.Confirmation = "D";
        var first = dialog.FormatAsync();

        var second = await dialog.FormatAsync();

        Assert.IsFalse(second);
        StringAssert.Contains(dialog.Error, "already being formatted");
        release.SetResult(new DriveActionResult(false, "stopped for the test"));
        Assert.IsFalse(await first);
    }

    [TestMethod]
    public void Load_AnUnknownFileSystem_FallsBackToNtfs()
    {
        var (dialog, _, _) = Create();

        dialog.Load("D:\\", "DATA", "not-a-filesystem");

        Assert.AreEqual("NTFS", dialog.FileSystem);
    }
}
