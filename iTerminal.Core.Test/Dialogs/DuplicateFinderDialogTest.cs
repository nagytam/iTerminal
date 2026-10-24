using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Duplicates;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

/// <summary>Ctrl+Shift+F7: mark the extra copies, ask, delete — and never leave a group without a copy.</summary>
[TestClass]
public class DuplicateFinderDialogTest
{
    private string _root = string.Empty;
    private static readonly DateTime Noon = new(2024, 5, 6, 12, 0, 0);

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "DuplicateFinderDialogTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        Write("one/report.txt", "same text", Noon);
        Write("two/report.txt", "same text", Noon.AddDays(1));
        Write("three/report.txt", "same text", Noon.AddDays(2));
        Write("unique.txt", "alone", Noon);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private void Write(string relative, string content, DateTime time)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, time);
    }

    private async Task<(DuplicateFinderDialog Dialog, Mock<IFileSystemService> FileSystem)> OpenAsync()
    {
        // A fake file system: a real delete would put the test's files in the machine's recycle bin.
        var fileSystem = new Mock<IFileSystemService>();
        fileSystem.Setup(f => f.DeleteFile(It.IsAny<Url>(), It.IsAny<bool>())).Returns(true);
        var dialog = new DuplicateFinderDialog(new DuplicateFinderService(), TestTheme(), Mock.Of<IContentModelService>(), fileSystem.Object);
        dialog.Load(_root, "LeftPanel");
        await ShowAsync(dialog);
        await dialog.Scan!.Completion;
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.Groups.Count > 0;
        }, "the groups to be listed");
        return (dialog, fileSystem);
    }

    private static Task PressAsync(DuplicateFinderDialog dialog, ConsoleKey key) => dialog.HandleKeyEventAsync(Key(key));

    [TestMethod]
    public async Task TheGroupIsListedWithItsCopies()
    {
        var (dialog, _) = await OpenAsync();

        var screen = Render(dialog.Parent!);

        AssertScreenContains(screen, "3 × 9");
        AssertScreenContains(screen, "wasted");
        AssertScreenContains(screen, "1 group");
        AssertScreenDoesNotContain(screen, "unique.txt");
    }

    [TestMethod]
    public async Task NMarksAllButTheNewest_ThatIsTheOneLeftUnmarked()
    {
        var (dialog, _) = await OpenAsync();
        var group = dialog.Groups.Single();
        var newest = group.Files.OrderByDescending(f => f.Modified).First();

        await PressAsync(dialog, ConsoleKey.N);

        Assert.AreEqual(group.Files.Count - 1, dialog.MarkedPaths.Count);
        Assert.IsFalse(dialog.MarkedPaths.Contains(newest.Path), "the newest copy is the one kept");
    }

    [TestMethod]
    public async Task AMarksAllButTheOldestAndDeleteAsksFirst()
    {
        var (dialog, fileSystem) = await OpenAsync();

        await PressAsync(dialog, ConsoleKey.A);
        Assert.AreEqual(2, dialog.MarkedPaths.Count);
        Assert.IsFalse(dialog.MarkedPaths.Any(p => p.Contains(Path.Combine("one", "report.txt"))), "the oldest is the one kept");

        await PressAsync(dialog, ConsoleKey.Delete);
        Assert.IsTrue(dialog.IsConfirmingDelete);
        StringAssert.Contains(dialog.Status, "y/n");
        fileSystem.Verify(f => f.DeleteFile(It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);

        // Anything but y withdraws it.
        await PressAsync(dialog, ConsoleKey.N);
        Assert.IsFalse(dialog.IsConfirmingDelete);
        fileSystem.Verify(f => f.DeleteFile(It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task ConfirmingDeletesTheMarkedFilesToTheRecycleBinAndTheGroupGoesAway()
    {
        var (dialog, fileSystem) = await OpenAsync();

        await PressAsync(dialog, ConsoleKey.A);
        await PressAsync(dialog, ConsoleKey.Delete);
        await PressAsync(dialog, ConsoleKey.Y);
        await dialog.DeleteTask;

        fileSystem.Verify(f => f.DeleteFile(It.IsAny<Url>(), true), Times.Exactly(2));
        Assert.AreEqual(0, dialog.Groups.Count, "one copy left: nothing is duplicated any more");
        Assert.AreEqual(0, dialog.MarkedPaths.Count);
        StringAssert.Contains(dialog.Status, "Deleted 2 files");
    }

    [TestMethod]
    public async Task TheLastUnmarkedCopyCannotBeMarked()
    {
        var (dialog, _) = await OpenAsync();

        // Down the three copies: Space marks and steps on; the third mark is refused.
        await PressAsync(dialog, ConsoleKey.Spacebar);
        await PressAsync(dialog, ConsoleKey.Spacebar);
        Assert.AreEqual(2, dialog.MarkedPaths.Count);
        await PressAsync(dialog, ConsoleKey.Spacebar);

        Assert.AreEqual(2, dialog.MarkedPaths.Count, "one copy of every group is always kept");
        StringAssert.Contains(dialog.Status, "at least one copy");
    }

    [TestMethod]
    public async Task DeletingWithNothingMarkedSaysSo()
    {
        var (dialog, fileSystem) = await OpenAsync();

        await PressAsync(dialog, ConsoleKey.Delete);

        Assert.IsFalse(dialog.IsConfirmingDelete);
        StringAssert.Contains(dialog.Status, "Nothing is marked");
        fileSystem.Verify(f => f.DeleteFile(It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [TestMethod]
    public async Task NMarksAllButTheNewest()
    {
        var (dialog, _) = await OpenAsync();

        await PressAsync(dialog, ConsoleKey.N);

        Assert.AreEqual(2, dialog.MarkedPaths.Count);
        Assert.IsFalse(dialog.MarkedPaths.Any(p => p.Contains(Path.Combine("three", "report.txt"))));
    }

    // The list starts after the dialog's border, the folder, the summary and a blank line.
    private static MouseEvent AtRow(DuplicateFinderDialog dialog, int row, int column = 20, bool click = true, int wheel = 0) => new()
    {
        Position = new Position(dialog.AbsolutePosition.X + 2 + column, dialog.AbsolutePosition.Y + 1 + 3 + row),
        LeftClicked = click && wheel == 0,
        WheelDelta = wheel,
    };

    [TestMethod]
    public async Task AClickOnAFile_PutsTheCursorOnIt()
    {
        var (dialog, _) = await OpenAsync();
        string first = dialog.Selected!.Path;

        Assert.IsTrue(await dialog.HandleMouseEventAsync(AtRow(dialog, 3)));

        Assert.AreNotEqual(first, dialog.Selected!.Path);
        Assert.AreEqual(dialog.Groups[0].Files[2].Path, dialog.Selected.Path);
        Assert.AreEqual(0, dialog.MarkedPaths.Count);
    }

    [TestMethod]
    public async Task AClickOnTheBox_MarksAndUnmarksThatFile()
    {
        var (dialog, _) = await OpenAsync();

        await dialog.HandleMouseEventAsync(AtRow(dialog, 1, column: 3));
        Assert.AreEqual(1, dialog.MarkedPaths.Count);
        Assert.AreEqual(dialog.Groups[0].Files[0].Path, dialog.MarkedPaths.Single());

        await dialog.HandleMouseEventAsync(AtRow(dialog, 1, column: 3));
        Assert.AreEqual(0, dialog.MarkedPaths.Count);
    }

    [TestMethod]
    public async Task AClickOnAGroupHeading_OrBelowTheList_ChangesNothing()
    {
        var (dialog, _) = await OpenAsync();
        string first = dialog.Selected!.Path;

        await dialog.HandleMouseEventAsync(AtRow(dialog, 0));
        await dialog.HandleMouseEventAsync(AtRow(dialog, 40));

        Assert.AreEqual(first, dialog.Selected!.Path);
        Assert.AreEqual(0, dialog.MarkedPaths.Count);
    }

    [TestMethod]
    public async Task TheWheel_MovesTheCursorThreeFilesAtATime_AndStopsAtTheEnds()
    {
        var (dialog, _) = await OpenAsync();

        Assert.IsTrue(await dialog.HandleMouseEventAsync(AtRow(dialog, 1, wheel: -1)));
        Assert.AreEqual(dialog.Groups[0].Files[2].Path, dialog.Selected!.Path);

        await dialog.HandleMouseEventAsync(AtRow(dialog, 1, wheel: 1));
        Assert.AreEqual(dialog.Groups[0].Files[0].Path, dialog.Selected!.Path);
        await dialog.HandleMouseEventAsync(AtRow(dialog, 1, wheel: 1));
        Assert.AreEqual(dialog.Groups[0].Files[0].Path, dialog.Selected!.Path);
    }

    [TestMethod]
    public async Task WhileTheDeletionIsBeingAsked_TheMouseLeavesTheListAlone()
    {
        var (dialog, _) = await OpenAsync();
        await PressAsync(dialog, ConsoleKey.A);
        await PressAsync(dialog, ConsoleKey.Delete);
        Assert.IsTrue(dialog.IsConfirmingDelete);
        string cursor = dialog.Selected!.Path;

        await dialog.HandleMouseEventAsync(AtRow(dialog, 3));

        Assert.AreEqual(cursor, dialog.Selected!.Path);
        Assert.IsTrue(dialog.IsConfirmingDelete);
    }

    [TestMethod]
    public async Task EnterGoesToTheFilesFolderInThePanel_AsGDoes_AndClosesTheDialog()
    {
        var panels = new Mock<IContentModelService>();
        var fileSystem = new Mock<IFileSystemService>();
        var dialog = new DuplicateFinderDialog(new DuplicateFinderService(), TestTheme(), panels.Object, fileSystem.Object);
        dialog.Load(_root, "LeftPanel");
        await ShowAsync(dialog);
        await dialog.Scan!.Completion;
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.Groups.Count > 0;
        }, "the groups to be listed");
        string folder = dialog.Selected!.Folder;
        AssertScreenContains(Render(dialog), "Enter or g:go to");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        panels.Verify(p => p.SetUrlAsync("LeftPanel", It.Is<Url>(u => u.FullName.TrimEnd(Path.DirectorySeparatorChar) == folder.TrimEnd(Path.DirectorySeparatorChar))), Times.Once);
        Assert.IsFalse(dialog.IsVisible);
    }
}
