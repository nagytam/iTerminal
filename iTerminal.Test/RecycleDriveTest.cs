using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Test;

/// <summary>
/// The recycle drive, end to end, on the sandbox's own bin (<see cref="TerminalApp.RecycleBinFolder"/>)
/// — never the bin of the machine the suite runs on: Ctrl+F8 lists what was deleted, Enter restores,
/// F8 deletes for good, and the deleted files can be looked at and copied out where they lie.
/// </summary>
[TestClass]
public class RecycleDriveTest
{
    private static readonly DateTime Monday = new(2026, 10, 5, 9, 30, 0, DateTimeKind.Local);

    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    /// <summary>The app with three things in its bin: a newer and an older file deleted from the left folder, and a folder.</summary>
    private async Task StartWithABinAsync()
    {
        _app = await TerminalApp.StartAsync(seed: (left, _) =>
        {
            var bin = Path.Combine(Path.GetDirectoryName(left)!, "bin");
            TerminalApp.PutInRecycleBin(bin, "AAA111", Path.Combine(left, "old report.txt"), "the old report", Monday);
            TerminalApp.PutInRecycleBin(bin, "BBB222", Path.Combine(left, "gone", "newer.md"), "# newer", Monday.AddDays(1));
            TerminalApp.PutInRecycleBin(bin, "CCC333", Path.Combine(left, "photos"), null, Monday.AddHours(2));
        });
    }

    private async Task<ContentModelRecycleDrive> OpenBinAsync()
    {
        await _app.PressAsync("Ctrl+F8");
        await _app.WaitUntilAsync(() => _app.FocusedModel is ContentModelRecycleDrive, "the focused panel to show the recycle bin");
        var drive = (ContentModelRecycleDrive)_app.FocusedModel!;
        await drive.ListingTask;
        return drive;
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlF8_ListsWhatWasDeleted_NewestFirst_AndPressedAgainGoesBack()
    {
        await StartWithABinAsync();

        await OpenBinAsync();

        var screen = await _app.WaitForTextAsync("old report.txt");
        Assert.IsTrue(screen.Contains("Recycle bin"), screen.Dump());
        Assert.IsTrue(screen.Contains("Enter:restore"), screen.Dump());
        Assert.IsTrue(screen.RowOf("newer.md") < screen.RowOf("photos"), screen.Dump());
        Assert.IsTrue(screen.RowOf("photos") < screen.RowOf("old report.txt"), screen.Dump());
        Assert.IsTrue(screen.Contains("2026-10-06"), "dated by when it was deleted" + Environment.NewLine + screen.Dump());

        // The info strip says where the row under the cursor was deleted from.
        await _app.SelectEntryAsync("old report.txt");
        await _app.WaitForTextAsync("Deleted from");

        await _app.PressAsync("Ctrl+F8");
        await _app.WaitForFocusedFolderAsync(_app.LeftRoot);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task Enter_RestoresTheFile_ToWhereItWasDeletedFrom()
    {
        await StartWithABinAsync();
        var drive = await OpenBinAsync();
        await _app.SelectEntryAsync("newer.md");

        await _app.PressAsync("Enter");
        await drive.RestoreTask;

        // Its folder had been deleted too, and is made again.
        Assert.AreEqual("# newer", File.ReadAllText(Path.Combine(_app.LeftRoot, "gone", "newer.md")));
        await _app.WaitForTextAsync("Restored newer.md");
        await _app.WaitUntilAsync(s => !_app.LeftPanelLists("newer.md", s), "the restored file to leave the bin's listing");
        Assert.IsTrue(_app.LeftPanelLists("old report.txt"), "the others are still there");
        Assert.AreEqual(4, Directory.GetFileSystemEntries(_app.RecycleBinFolder).Length, "two items left, a record and its content each");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task Enter_WithRowsMarked_RestoresThemAll_FoldersIncluded()
    {
        await StartWithABinAsync();
        var drive = await OpenBinAsync();
        await _app.SelectEntryAsync("photos");
        await _app.PressAsync("Insert");
        await _app.SelectEntryAsync("old report.txt");
        await _app.PressAsync("Insert");

        await _app.PressAsync("Enter");
        await drive.RestoreTask;

        Assert.AreEqual("the old report", File.ReadAllText(Path.Combine(_app.LeftRoot, "old report.txt")));
        Assert.AreEqual("inside the deleted folder", File.ReadAllText(Path.Combine(_app.LeftRoot, "photos", "inside.txt")));
        await _app.WaitForTextAsync("Restored 2 items");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task Restore_NeverOverwrites_AndSaysWhy()
    {
        await StartWithABinAsync();
        File.WriteAllText(Path.Combine(_app.LeftRoot, "old report.txt"), "a newer file of the same name");
        var drive = await OpenBinAsync();
        await _app.SelectEntryAsync("old report.txt");

        await _app.PressAsync("Enter");
        await drive.RestoreTask;

        await _app.WaitForTextAsync("already exists");
        Assert.AreEqual("a newer file of the same name", File.ReadAllText(Path.Combine(_app.LeftRoot, "old report.txt")));
        Assert.IsTrue(_app.LeftPanelLists("old report.txt"), "and the deleted one is still in the bin");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F8_DeletesForGood_AfterTheDialogSaysSo()
    {
        await StartWithABinAsync();
        await OpenBinAsync();
        await _app.SelectEntryAsync("old report.txt");

        await _app.PressAsync("F8");
        var dialog = await _app.Driver.WaitForModalAsync<DeleteDialog>();
        Assert.IsFalse(dialog.MoveToRecycleBin, "there is no bin to move to from the bin");
        await _app.PressAsync("Enter");

        await _app.WaitForNoDialogAsync();
        await _app.WaitUntilAsync(
            () => !Directory.EnumerateFileSystemEntries(_app.RecycleBinFolder).Any(path => path.Contains("AAA111")),
            "the file and its record to be gone from the bin");
        await _app.WaitUntilAsync(s => !_app.LeftPanelLists("old report.txt", s), "the row to leave the listing");
        Assert.IsFalse(File.Exists(Path.Combine(_app.LeftRoot, "old report.txt")), "deleted for good is not restored");
        Assert.IsTrue(_app.LeftPanelLists("photos"), "the rest of the bin is untouched");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task MarkAllThenF8_EmptiesTheBin()
    {
        await StartWithABinAsync();
        await OpenBinAsync();
        await _app.SelectEntryAsync("newer.md");
        await _app.PressAsync("NumStar");

        await _app.PressAsync("F8");
        await _app.Driver.WaitForModalAsync<DeleteDialog>();
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => Directory.GetFileSystemEntries(_app.RecycleBinFolder).Length == 0, "the bin to be empty");
        await _app.WaitForNoDialogAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ADeletedFile_CanBeViewedAndCopiedOut_WithoutRestoringIt()
    {
        await StartWithABinAsync();
        await OpenBinAsync();
        await _app.SelectEntryAsync("old report.txt");

        await _app.PressAsync("F3");
        await _app.WaitForTextAsync("the old report");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");

        var copy = Path.Combine(_app.RightRoot, "old report.txt");
        await _app.WaitUntilAsync(() => File.Exists(copy) && new FileInfo(copy).Length > 0, "the copy to be written to the other panel");
        Assert.AreEqual("the old report", File.ReadAllText(copy));
        Assert.IsTrue(File.Exists(Path.Combine(_app.RecycleBinFolder, "$RAAA111.txt")), "copying out leaves it in the bin");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task OnTheParentRow_EnterLeavesTheDrive()
    {
        await StartWithABinAsync();
        await OpenBinAsync();
        await _app.PressAsync("Home");

        await _app.PressAsync("Enter");

        await _app.WaitForFocusedFolderAsync(_app.LeftRoot);
        Assert.AreEqual(6, Directory.GetFileSystemEntries(_app.RecycleBinFolder).Length, "nothing was restored on the way out");
    }
}
