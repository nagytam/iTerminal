using iConsole.Controls;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services.Drives;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>E (eject) and F (format) in the disk manager: what each says when it cannot do what was asked.</summary>
[TestClass]
public class DiskManagerActionsTest
{
    private sealed class Rig : IDisposable
    {
        public required DiskManagerDialog Dialog { get; init; }

        public required Mock<IDriveActionsService> Drives { get; init; }

        public required TaskManager Tasks { get; init; }

        public void Dispose() => Tasks.Dispose();
    }

    private static async Task<Rig> OpenAsync(Drive drive, bool canFormat = true, bool withFormatDialog = true)
    {
        TaskManager tasks = new();
        DriveInfoModel model = new();
        model.Drives.Add(drive);
        model.IsLoaded = true;
        Mock<IDriveActionsService> drives = new();
        drives.SetupGet(d => d.CanFormat).Returns(canFormat);
        drives.Setup(d => d.EjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DriveActionResult(true, "Ejected"));
        drives.Setup(d => d.GetBitLockerStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BitLockerStatus(BitLockerState.Off, "Off"));
        FormatDriveDialog? format = withFormatDialog
            ? new FormatDriveDialog(drives.Object, Mock.Of<INotificationService>(), Mock.Of<IContentModelService>(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme())
            : null;
        DiskManagerDialog dialog = new(tasks, model, TestTheme(), drives.Object, Mock.Of<IContentModelService>(), Mock.Of<INotificationService>(), format);
        await ShowAsync(dialog);
        dialog.OnIdle();
        return new Rig { Dialog = dialog, Drives = drives, Tasks = tasks };
    }

    private static Drive MemoryDrive() => new() { Name = Container.MEMORY_DRIVE + "work", Url = new Url(Container.MEMORY_DRIVE + "work/") };

    private static Drive RealDrive()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;
        return new Drive { Name = root, Url = new Url(root), DriveInfo = new DriveInfo(root) };
    }

    [TestMethod]
    public async Task EjectingAMemoryDrive_SaysThereIsNothingToEject()
    {
        using Rig rig = await OpenAsync(MemoryDrive());

        await rig.Dialog.EjectAsync();

        StringAssert.Contains(rig.Dialog.Status, "memory drive");
        StringAssert.Contains(rig.Dialog.Status, "nothing to eject");
        rig.Drives.Verify(d => d.EjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task FormattingAMemoryDrive_SaysItCannotBe()
    {
        using Rig rig = await OpenAsync(MemoryDrive());

        await rig.Dialog.FormatAsync();

        StringAssert.Contains(rig.Dialog.Status, "memory drive cannot be formatted");
    }

    [TestMethod]
    public async Task F_OnASystemThatCannotFormat_SaysSoInsteadOfAskingForADrive()
    {
        using Rig rig = await OpenAsync(RealDrive(), canFormat: false);

        await rig.Dialog.HandleKeyEventAsync(Char('f'));

        Assert.AreEqual("Formatting is not available on this system.", rig.Dialog.Status);
    }

    [TestMethod]
    public async Task Format_WithoutADialogToShow_SaysItIsNotAvailable()
    {
        using Rig rig = await OpenAsync(RealDrive(), withFormatDialog: false);

        await rig.Dialog.FormatAsync();

        Assert.AreEqual("Formatting is not available on this system.", rig.Dialog.Status);
    }

    [TestMethod]
    public async Task Format_WithNoDriveListed_AsksForOne()
    {
        TaskManager tasks = new();
        DriveInfoModel model = new() { IsLoaded = true };
        Mock<IDriveActionsService> drives = new();
        drives.SetupGet(d => d.CanFormat).Returns(true);
        FormatDriveDialog format = new(drives.Object, Mock.Of<INotificationService>(), Mock.Of<IContentModelService>(), Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), TestTheme());
        DiskManagerDialog dialog = new(tasks, model, TestTheme(), drives.Object, Mock.Of<IContentModelService>(), Mock.Of<INotificationService>(), format);
        await ShowAsync(dialog);

        await dialog.FormatAsync();
        Assert.AreEqual("Choose a drive to format.", dialog.Status);
        await dialog.EjectAsync();
        Assert.AreEqual("Choose a drive to eject.", dialog.Status);

        tasks.Dispose();
    }

    [TestMethod]
    public async Task WhileAnEjectIsRunning_AnotherRequestIsRefusedWithAMessage()
    {
        TaskCompletionSource<DriveActionResult> gate = new();
        using Rig rig = await OpenAsync(RealDrive());
        rig.Drives.Setup(d => d.EjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(gate.Task);

        Task first = rig.Dialog.EjectAsync();
        StringAssert.Contains(rig.Dialog.Status, "Ejecting");

        // The second request answers with the first one's task, which is not over: it is not awaited.
        _ = rig.Dialog.EjectAsync();
        Assert.AreEqual("Still working on the last request.", rig.Dialog.Status);
        await rig.Dialog.FormatAsync();
        Assert.AreEqual("Still working on the last request.", rig.Dialog.Status);

        gate.SetResult(new DriveActionResult(true, "Ejected"));
        await first;
        Assert.AreEqual("Ejected", rig.Dialog.Status);
        rig.Drives.Verify(d => d.EjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AFootersHintsLeaveOutFormatWhereItDoesNotExist()
    {
        using Rig withFormat = await OpenAsync(RealDrive());
        AssertScreenContains(Render(withFormat.Dialog), "F:format");

        using Rig without = await OpenAsync(RealDrive(), canFormat: false);
        AssertScreenDoesNotContain(Render(without.Dialog), "F:format");
        AssertScreenContains(Render(without.Dialog), "E:eject");
    }
}
