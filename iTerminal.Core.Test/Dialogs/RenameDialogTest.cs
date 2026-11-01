using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>F6: rename the focused entry.</summary>
[TestClass]
public class RenameDialogTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _folder = Path.Combine(Path.GetTempPath(), "RenameDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private (RenameDialog Dialog, Mock<IContentModel> Model) Create(string entry)
    {
        var model = new Mock<IContentModel>();
        model.Setup(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>())).Returns(Task.CompletedTask);
        var contentModelService = new Mock<IContentModelService>();
        contentModelService.Setup(s => s.GetLastContentModel()).Returns(model.Object);

        var dialog = new RenameDialog(Mock.Of<IBeepService>(), new DateTimeProviderService(), contentModelService.Object, TestTheme())
        {
            BaseUrl = new Url(_folder),
            SourceUrl = new Url(Path.Combine(_folder, entry)),
            NewName = entry,
        };
        return (dialog, model);
    }

    private static Task PressRenameAsync(RenameDialog dialog) =>
        dialog.Controls.AsSnapshot().OfType<Button>().Single(b => b.Title == "Rename").TriggerHotKeyAsync();

    [Timeout(60_000)]
    [TestMethod]
    public void ABareNameRenamesTheFileWhereItIs()
    {
        // The base used to be the entry itself, so a bare name renamed a file to itself.
        var (dialog, _) = Create("report.txt");

        dialog.NewName = "summary.txt";

        Assert.AreEqual(Path.Combine(_folder, "summary.txt"), dialog.TargetUrl.FullName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ABareNameRenamesAFolderBesideItselfNotInsideIt()
    {
        var (dialog, _) = Create("sub");

        dialog.NewName = "renamed";

        Assert.AreEqual(Path.Combine(_folder, "renamed"), dialog.TargetUrl.FullName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFullPathIsTakenAsTyped()
    {
        var (dialog, _) = Create("report.txt");
        var elsewhere = Path.Combine(_folder, "sub", "moved.txt");

        dialog.NewName = elsewhere;

        Assert.AreEqual(elsewhere, dialog.TargetUrl.FullName);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensWithJustTheNameToEdit()
    {
        var (dialog, _) = Create("report.txt");
        await ShowAsync(dialog);

        Assert.AreEqual("report.txt", dialog.NewName, "the box holds the name, not the whole URL");
        AssertScreenContains(Render(dialog), "report.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RenamingMovesTheEntryAndCloses()
    {
        var (dialog, model) = Create("report.txt");
        var root = await ShowAsync(dialog);
        dialog.NewName = "summary.txt";

        await PressRenameAsync(dialog);

        model.Verify(m => m.MoveAsync(
            It.Is<Url>(u => u.FullName == Path.Combine(_folder, "report.txt")),
            It.Is<Url>(u => u.FullName == Path.Combine(_folder, "summary.txt")),
            false), Times.Once);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnchangedNameJustCloses()
    {
        var (dialog, model) = Create("report.txt");
        var root = await ShowAsync(dialog);

        await PressRenameAsync(dialog);

        model.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEmptyNameIsRefusedWithAMessage()
    {
        var (dialog, model) = Create("report.txt");
        var root = await ShowAsync(dialog);
        dialog.NewName = string.Empty;

        await PressRenameAsync(dialog);

        Assert.AreEqual("Type the new name.", dialog.ErrorMessage.Text);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
        model.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedRenameStaysOpenAndShowsWhy()
    {
        var (dialog, model) = Create("report.txt");
        model.Setup(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>())).ThrowsAsync(new IOException("file in use"));
        var root = await ShowAsync(dialog);
        dialog.NewName = "summary.txt";

        await PressRenameAsync(dialog);

        Assert.AreEqual("file in use", dialog.ErrorMessage.Text);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EscapeClosesIt()
    {
        var (dialog, _) = Create("report.txt");
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
