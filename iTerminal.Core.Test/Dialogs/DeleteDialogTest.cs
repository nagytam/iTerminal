using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>F8: delete the focused entry.</summary>
[TestClass]
public class DeleteDialogTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "DeleteDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_folder, "sub"));
        File.WriteAllText(Path.Combine(_folder, "report.txt"), "x");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch
        {
        }
    }

    private static DeleteDialog Create(string path)
    {
        var entry = new Mock<IEntry>();
        entry.SetupGet(e => e.Url).Returns(new Url(path));
        var model = new Mock<IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetFocusedModel()).Returns(model.Object);

        return new DeleteDialog(
            contentModelService.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(),
            null!, null!, null!, null!, null!, null!, null!);
    }

    [TestMethod]
    public async Task AFileIsNamedAndNotOfferedARecursiveDelete()
    {
        var dialog = Create(Path.Combine(_folder, "report.txt"));
        await ShowAsync(dialog);

        Assert.AreEqual("Delete file", dialog.Title);
        Assert.IsFalse(dialog.Recursive);
        StringAssert.EndsWith(dialog.Url, "report.txt");
    }

    [TestMethod]
    public async Task AFolderIsDeletedRecursivelyByDefault()
    {
        var dialog = Create(Path.Combine(_folder, "sub"));
        await ShowAsync(dialog);

        Assert.AreEqual("Delete folder", dialog.Title);
        Assert.IsTrue(dialog.Recursive);
    }

    [TestMethod]
    public async Task ShowsWhatWillBeDeletedAndTheButtons()
    {
        var dialog = Create(Path.Combine(_folder, "report.txt"));
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "report.txt");
        AssertScreenContains(screen, OperatingSystem.IsLinux() ? "Move to Trash" : "Move to Recycle bin");
        AssertScreenContains(screen, "Delete");
        AssertScreenContains(screen, "Cancel", "the Cancel button reads Cancel, not a cut-off command name");
    }

    [TestMethod]
    public async Task ResetClearsTheError()
    {
        var dialog = Create(Path.Combine(_folder, "report.txt"));
        await ShowAsync(dialog);
        dialog.ErrorMessage.Text = "boom";

        dialog.Reset();

        Assert.AreEqual(string.Empty, dialog.ErrorMessage.Text);
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var dialog = Create(Path.Combine(_folder, "report.txt"));
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [TestMethod]
    public async Task ALocalDeleteGoesToTheRecycleBinByDefaultWhereThereIsOne()
    {
        // It started unchecked: a permanent delete one Enter away.
        var dialog = Create(Path.Combine(_folder, "report.txt"));
        await ShowAsync(dialog);

        Assert.AreEqual(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), dialog.MoveToRecycleBin);
    }

    [TestMethod]
    public async Task OnADriveWithoutARecycleBinTheBoxSaysTheDeleteIsPermanent()
    {
        // The box stayed tickable on drives that ignore it, promising a way back that was not there.
        var dialog = Create("ssh://alice@host:/home/alice/report.txt");
        await ShowAsync(dialog);

        Assert.IsFalse(dialog.MoveToRecycleBin);
        AssertScreenContains(Render(dialog), "deleted for good");
    }

    [TestMethod]
    public void OnlyLocalFilesOnWindowsAndLinuxCanBeRecycled()
    {
        Assert.AreEqual(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), DeleteDialog.CanRecycle(new Url(Path.GetTempPath())));
        Assert.IsFalse(DeleteDialog.CanRecycle(new Url("memdrive://notes.txt")));
    }
}
