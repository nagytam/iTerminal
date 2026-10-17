namespace iTerminal.Test;

/// <summary>
/// Create, copy, move, rename and delete, end to end: the key opens the dialog, the dialog's Enter
/// runs the operation on a background task, the file system changes, and the panels show it.
/// </summary>
[TestClass]
public class FileOperationsTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    /// <summary>The file's text, or null while it is missing or still held open by the operation writing it.</summary>
    private static string? ReadIfFinished(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task CreateFolderAsync(string name)
    {
        await _app.PressAsync("Shift+F7");
        await _app.WaitForTextAsync("Create folder");
        await _app.TypeAsync(name);
        await _app.PressAsync("Enter");
    }

    [TestMethod]
    public async Task ShiftF7CreatesAFolder()
    {
        await CreateFolderAsync("gamma");

        await _app.WaitUntilAsync(() => Directory.Exists(Path.Combine(_app.LeftRoot, "gamma")), "the folder to exist");
        await _app.WaitForNoDialogAsync();
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("gamma", s), "the new folder to be listed");
        await _app.WaitForNoStrayFramesAsync();
    }

    /// <summary>Text boxes used to accept ASCII letters, digits and six punctuation marks only.</summary>
    [TestMethod]
    public async Task AFolderNameMayHoldPunctuation()
    {
        await CreateFolderAsync("Photos (2024), Tom's & more");

        await _app.WaitUntilAsync(() => Directory.Exists(Path.Combine(_app.LeftRoot, "Photos (2024), Tom's & more")),
            "a folder named with parentheses, a comma, an apostrophe and an ampersand");
    }

    [TestMethod]
    public async Task AFolderNameMayHoldAccentedLetters()
    {
        await CreateFolderAsync("Árvíztűrő tükörfúrógép");

        await _app.WaitUntilAsync(() => Directory.Exists(Path.Combine(_app.LeftRoot, "Árvíztűrő tükörfúrógép")),
            "a folder with a Hungarian name");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("Árvíztűrő tükörfúrógép", s), "the accented name to be drawn");
    }

    /// <summary>The empty field used to say "Supply path!" in red before a key was pressed.</summary>
    [TestMethod]
    public async Task TheCreateDialogOpensWithoutAnError()
    {
        await _app.PressAsync("Shift+F7");

        var screen = await _app.WaitForTextAsync("Create folder");
        Assert.IsFalse(screen.Contains("Error"), screen.Dump());
        Assert.IsFalse(screen.Contains("Supply path"), screen.Dump());
    }

    [TestMethod]
    public async Task EnterOnAnEmptyNameCreatesNothingAndKeepsTheDialog()
    {
        await _app.PressAsync("Shift+F7");
        await _app.WaitForTextAsync("Create folder");

        await _app.PressAsync("Enter");

        await _app.Driver.WaitForIdleAsync();
        Assert.IsInstanceOfType<Core.Dialogs.CreateFolderDialog>(_app.Driver.FocusedModal);
        Assert.AreEqual(2, Directory.GetDirectories(_app.LeftRoot).Length, "still only alpha and beta");
    }

    /// <summary>"Error: Error: Path exists already!" — the message carried the prefix the line adds.</summary>
    [TestMethod]
    public async Task ATakenNameSaysSoOnce()
    {
        await _app.PressAsync("Shift+F7");
        await _app.WaitForTextAsync("Create folder");

        await _app.TypeAsync("alpha");

        var screen = await _app.WaitForTextAsync("already exists");
        Assert.IsFalse(screen.Contains("Error: Error"), screen.Dump());
        await _app.PressAsync("Enter");
        Assert.IsInstanceOfType<Core.Dialogs.CreateFolderDialog>(_app.Driver.FocusedModal, "a taken name is not created over");
    }

    [TestMethod]
    public async Task EscCancelsTheCreateDialog()
    {
        await _app.PressAsync("Shift+F7");
        await _app.WaitForTextAsync("Create folder");
        await _app.TypeAsync("never");

        await _app.PressAsync("Esc");

        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextGoneAsync("Create folder");
        Assert.IsFalse(Directory.Exists(Path.Combine(_app.LeftRoot, "never")));
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task ShiftF4CreatesAFile()
    {
        await _app.PressAsync("Shift+F4");
        await _app.WaitForTextAsync("Create file");
        await _app.TypeAsync("todo.txt");

        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.LeftRoot, "todo.txt")), "the file to exist");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("todo.txt", s), "the new file to be listed");
    }

    [TestMethod]
    public async Task F5CopiesTheFileUnderTheCursorToTheOtherPanel()
    {
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");

        // The file exists from the moment the copy starts writing it, held open until it is done:
        // waited for with its whole content, not for existing.
        var copy = Path.Combine(_app.RightRoot, "readme.txt");
        var original = File.ReadAllText(Path.Combine(_app.LeftRoot, "readme.txt"));
        await _app.WaitUntilAsync(() => ReadIfFinished(copy) == original, "the copy to be complete");
        Assert.IsTrue(File.Exists(Path.Combine(_app.LeftRoot, "readme.txt")), "a copy leaves the source");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("readme.txt", s), "the target panel to list the copy");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F5CopiesAFolderWithItsContents()
    {
        await _app.SelectEntryAsync("alpha");

        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "alpha", "inside_alpha.txt")), "the folder's file to be copied");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("alpha", s), "the target panel to list the folder");
    }

    [TestMethod]
    public async Task F5CopiesAFileToTheMemoryDrive()
    {
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url("memdrive://"));
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");

        var memory = _app.Driver.GetRequiredService<Core.Services.IMemoryDriveService>();
        await _app.WaitUntilAsync(() => memory.ExistsFile(new iConsole.Data.Url("memdrive://readme.txt")), "the file to land on the memory drive");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("readme.txt", s), "the memory panel to list the copy");
    }

    private Core.Services.IMemoryDriveService Memory => _app.Driver.GetRequiredService<Core.Services.IMemoryDriveService>();

    private async Task PutInMemoryAsync(string url, string text) =>
        await Memory.WriteFileAsync(new iConsole.Data.Url(url), new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));

    private async Task CopyFocusedEntryAsync()
    {
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");
    }

    [TestMethod]
    public async Task F5CopiesAFileFromTheMemoryDriveToTheDisk()
    {
        await PutInMemoryAsync("memdrive://note.txt", "from memory");
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url("memdrive://"));
        await _app.SelectEntryAsync("note.txt");

        await CopyFocusedEntryAsync();

        await _app.WaitUntilAsync(() => ReadIfFinished(Path.Combine(_app.RightRoot, "note.txt")) == "from memory", "the file to land on the disk");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F5CopiesAFolderFromTheMemoryDriveToTheDisk()
    {
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://box/"));
        await PutInMemoryAsync("memdrive://box/one.txt", "one");
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://box/inner/"));
        await PutInMemoryAsync("memdrive://box/inner/two.txt", "two");
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url("memdrive://"));
        await _app.SelectEntryAsync("box");

        await CopyFocusedEntryAsync();

        await _app.WaitUntilAsync(() => ReadIfFinished(Path.Combine(_app.RightRoot, "box", "inner", "two.txt")) == "two", "the nested file to land on the disk");
        Assert.AreEqual("one", ReadIfFinished(Path.Combine(_app.RightRoot, "box", "one.txt")));
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F5CopiesAFileWithinTheMemoryDrive()
    {
        await PutInMemoryAsync("memdrive://note.txt", "from memory");
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://docs/"));
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url("memdrive://"));
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url("memdrive://docs/"));
        await _app.SelectEntryAsync("note.txt");

        await CopyFocusedEntryAsync();

        await _app.WaitUntilAsync(() => Memory.ExistsFile(new iConsole.Data.Url("memdrive://docs/note.txt")), "the file to land in the memory folder");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F5CopiesAFolderWithinTheMemoryDrive()
    {
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://box/"));
        await PutInMemoryAsync("memdrive://box/one.txt", "one");
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://docs/"));
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url("memdrive://"));
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url("memdrive://docs/"));
        await _app.SelectEntryAsync("box");

        await CopyFocusedEntryAsync();

        await _app.WaitUntilAsync(() => Memory.ExistsFile(new iConsole.Data.Url("memdrive://docs/box/one.txt")), "the folder to land in the memory folder");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F5CopiesABigFileToTheMemoryDriveAndShowsProgress()
    {
        var big = new byte[40 * 1024 * 1024];
        new Random(1).NextBytes(big);
        File.WriteAllBytes(Path.Combine(_app.LeftRoot, "big.bin"), big);
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url("memdrive://"));
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url(_app.LeftRoot));
        await _app.SelectEntryAsync("big.bin");

        await CopyFocusedEntryAsync();

        await _app.WaitUntilAsync(() => Memory.ExistsFile(new iConsole.Data.Url("memdrive://big.bin")), "the big file to land on the memory drive");
        await _app.WaitForNoDialogAsync();
    }

    /// <summary>The memory drive loads what it mirrored last time after the panel may already have listed it.</summary>
    [TestMethod]
    public async Task TheMemoryPanelListsFilesMirroredFromAnEarlierRun()
    {
        await _app.DisposeAsync();
        _app = await TerminalApp.StartAsync((left, _) =>
        {
            var memory = Path.Combine(Path.GetDirectoryName(left)!, "memory");
            Directory.CreateDirectory(memory);
            File.WriteAllText(Path.Combine(memory, "kept.txt"), "kept");
        });

        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url("memdrive://"));

        await _app.WaitUntilAsync(s => _app.RightPanelLists("kept.txt", s), "the memory panel to list the mirrored file");
    }

    [TestMethod]
    public async Task F5CopiesALargeNestedFolderToTheMemoryDrive()
    {
        var year = Path.Combine(_app.LeftRoot, "2025");
        for (var month = 1; month <= 12; month++)
        {
            var folder = Path.Combine(year, $"month {month:00}", "scans");
            Directory.CreateDirectory(folder);
            for (var i = 0; i < 30; i++)
            {
                File.WriteAllBytes(Path.Combine(folder, $"doc {i:000} (copy).pdf"), new byte[(i + 1) * 20_000]);
            }
        }
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url("memdrive://"));
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url(_app.LeftRoot));
        await _app.SelectEntryAsync("2025");

        await CopyFocusedEntryAsync();

        await _app.WaitUntilAsync(() => Memory.ExistsFile(new iConsole.Data.Url("memdrive://2025/month 12/scans/doc 029 (copy).pdf")), "the last file to land on the memory drive");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F5CopiesAFolderToTheMemoryDrive()
    {
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url("memdrive://"));
        await _app.SelectEntryAsync("alpha");

        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");

        var memory = _app.Driver.GetRequiredService<Core.Services.IMemoryDriveService>();
        await _app.WaitUntilAsync(() => memory.ExistsFile(new iConsole.Data.Url("memdrive://alpha/inside_alpha.txt")), "the folder's file to land on the memory drive");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("alpha", s), "the memory panel to list the folder");
    }

    [TestMethod]
    public async Task ShiftF6MovesTheFileToTheOtherPanel()
    {
        await _app.SelectEntryAsync("notes.md");

        await _app.PressAsync("Shift+F6");
        await _app.WaitForTextAsync("Move");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "notes.md")), "the file to arrive");
        await _app.WaitUntilAsync(() => !File.Exists(Path.Combine(_app.LeftRoot, "notes.md")), "the source to be gone");
        await _app.WaitUntilAsync(s => _app.RightPanelLists("notes.md", s) && !_app.LeftPanelLists("notes.md", s),
            "both panels to show the move");
    }

    [TestMethod]
    public async Task F6RenamesTheFile()
    {
        await _app.SelectEntryAsync("notes.md");

        await _app.PressAsync("F6");
        await _app.WaitForTextAsync("notes.md");
        await _app.PressAsync("End");
        foreach (var _ in "notes.md")
        {
            await _app.PressAsync("Backspace");
        }
        await _app.TypeAsync("journal.md");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.LeftRoot, "journal.md")), "the file under its new name");
        Assert.IsFalse(File.Exists(Path.Combine(_app.LeftRoot, "notes.md")));
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("journal.md", s) && !_app.LeftPanelLists("notes.md", s), "the panel to show the new name");
    }

    [TestMethod]
    public async Task F8DeletesAFilePermanentlyWhenTheRecycleBinIsUnticked()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The Recycle Bin option is a Windows and Linux feature.");
        }

        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("Delete file");
        await _app.PressAsync("Tab");      // onto the recycle box
        await _app.PressAsync("Space");    // untick it: the test must not fill the user's Recycle Bin
        await _app.WaitForTextAsync("□ " + BinLabel);
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => !File.Exists(Path.Combine(_app.LeftRoot, "readme.txt")), "the file to be deleted");
        await _app.WaitUntilAsync(s => !_app.LeftPanelLists("readme.txt", s), "the panel to drop it");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F8DeletesAFolderTreeOnTheMemoryDrive()
    {
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://box/"));
        await PutInMemoryAsync("memdrive://box/one.txt", "one");
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://box/inner/"));
        await PutInMemoryAsync("memdrive://box/inner/two.txt", "two");
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url("memdrive://"));
        await _app.SelectEntryAsync("box");

        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("Delete");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => !Memory.ExistsFolder(new iConsole.Data.Url("memdrive://box/")), "the folder tree to be deleted");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task F8DeletesAnEmptyFolderOnTheMemoryDrive()
    {
        await Memory.CreateFolderAsync(new iConsole.Data.Url("memdrive://empty/"));
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Left, new iConsole.Data.Url("memdrive://"));
        await _app.SelectEntryAsync("empty");

        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("Delete");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => !Memory.ExistsFolder(new iConsole.Data.Url("memdrive://empty/")), "the folder to be deleted");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task OnALocalFolderTheRecycleBinIsOfferedAndTicked()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The Recycle Bin is a Windows and Linux feature.");
        }

        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F8");

        await _app.WaitForTextAsync("▣ " + BinLabel);
    }

    /// <summary>What the Delete dialog calls the place a delete can be undone from, on this system.</summary>
    private static string BinLabel => OperatingSystem.IsLinux() ? "Move to Trash" : "Move to Recycle bin";

    [TestMethod]
    public async Task EscCancelsTheDeleteAndKeepsTheFile()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("Delete file");

        await _app.PressAsync("Esc");

        await _app.WaitForNoDialogAsync();
        Assert.IsTrue(File.Exists(Path.Combine(_app.LeftRoot, "readme.txt")));
        await _app.WaitForTextGoneAsync("Delete file");
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task OperationsOnTheParentEntryDoNothing()
    {
        await _app.PressAsync("Home");

        Assert.IsFalse(await _app.PressAsync("F8"), "nothing to delete on ..");
        Assert.IsFalse(await _app.PressAsync("F5"), "nothing to copy on ..");
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal);
    }
}
