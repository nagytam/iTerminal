using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Tasks;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class DiskManagerDialogTest
{
    [TestMethod]
    public async Task ListsTheDrives()
    {
        using var taskManager = new TaskManager();
        var model = new DriveInfoModel();
        model.Drives.Add(new Drive { Name = "C:", Url = new Url(Path.GetTempPath()) });
        var dialog = new DiskManagerDialog(taskManager, model, TestTheme());
        await ShowAsync(dialog);

        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return Render(dialog).Contains("C:");
        }, "the drive to be listed");
        Assert.AreEqual("Disk manager", dialog.Title);
    }

    [TestMethod]
    public async Task NoDrivesIsSaidRatherThanLeftBlank()
    {
        using var taskManager = new TaskManager();
        var model = new DriveInfoModel();
        var dialog = new DiskManagerDialog(taskManager, model, TestTheme());
        await ShowAsync(dialog);

        // Before the first pass an empty list means "not read yet" — saying "none" then is wrong.
        dialog.OnIdle();
        AssertScreenContains(Render(dialog), "Reading the drives");

        model.IsLoaded = true;
        dialog.OnIdle();
        AssertScreenContains(Render(dialog), "No drives found.");
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        using var taskManager = new TaskManager();
        var dialog = new DiskManagerDialog(taskManager, new DriveInfoModel(), TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task AFailedDetailRead_IsSaidOnThePane_NotLeftAsReading()
    {
        using var taskManager = new TaskManager();
        var model = new DriveInfoModel { IsLoaded = true };
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        model.Drives.Add(new Drive { Name = root, Url = new Url(root), DriveInfo = new System.IO.DriveInfo(root) });
        var drives = new Moq.Mock<iTerminal.Core.Services.Drives.IDriveActionsService>();
        drives.Setup(d => d.GetBitLockerStatusAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("BitLocker query refused"));
        var dialog = new DiskManagerDialog(taskManager, model, TestTheme(), drives.Object);
        await ShowAsync(dialog);

        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return Render(dialog).Contains("Could not read");
        }, "the failed read to be said on the pane");
        AssertScreenDoesNotContain(Render(dialog), "Reading...");
    }
}
