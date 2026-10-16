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
}
