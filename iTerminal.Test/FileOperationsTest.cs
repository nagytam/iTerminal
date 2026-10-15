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
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("Delete file");
        await _app.PressAsync("Tab");      // onto "Move to Recycle bin"
        await _app.PressAsync("Space");    // untick it: the test must not fill the user's Recycle Bin
        await _app.WaitForTextAsync("□ Move to Recycle bin");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => !File.Exists(Path.Combine(_app.LeftRoot, "readme.txt")), "the file to be deleted");
        await _app.WaitUntilAsync(s => !_app.LeftPanelLists("readme.txt", s), "the panel to drop it");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task OnALocalFolderTheRecycleBinIsOfferedAndTicked()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Recycle Bin is a Windows feature.");
        }

        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F8");

        await _app.WaitForTextAsync("▣ Move to Recycle bin");
    }

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
