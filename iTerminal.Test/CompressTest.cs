using System.IO.Compression;

namespace iTerminal.Test;

/// <summary>Alt+F5, end to end: mark, open the dialog, choose the format, and the archive appears in the other panel.</summary>
[TestClass]
public class CompressTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private static string[] ZipEntries(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return [.. zip.Entries.Select(e => e.FullName)];
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return [];
        }
    }

    [TestMethod]
    public async Task AltF5PacksTheMarkedRowsIntoAZipInTheOtherPanel()
    {
        await _app.SelectEntryAsync("alpha");
        await _app.PressAsync("Insert");
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Insert");

        await _app.PressAsync("Alt+F5");
        await _app.WaitForTextAsync("Compress 2 items");
        var archive = Path.Combine(_app.RightRoot, Path.GetFileName(_app.LeftRoot) + ".zip");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => ZipEntries(archive).Contains("alpha/inside_alpha.txt") && ZipEntries(archive).Contains("readme.txt"),
            "the archive to hold the folder's file and the marked file");
        await _app.WaitUntilAsync(s => _app.RightPanelLists(Path.GetFileName(archive), s), "the other panel to list the archive");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextGoneAsync("2 marked");
    }

    [TestMethod]
    public async Task TypingTheEndingChoosesTheFormat()
    {
        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("Alt+F5");
        await _app.WaitForTextAsync("Compress notes.md");

        // The cursor is at the end of the path: swap ".zip" for ".tar.gz".
        for (var i = 0; i < ".zip".Length; i++)
        {
            await _app.PressAsync("Backspace");
        }

        await _app.TypeAsync(".tar.gz");
        await _app.WaitForTextAsync("TAR.GZ");
        await _app.WaitForTextAsync("takes no password");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "notes.tar.gz")), "the TAR.GZ to be written");
        await _app.WaitForNoDialogAsync();
    }

    /// <summary>Alt+F5 one way, Shift+F5 back: the archive packed into the right panel unpacks into the left, into a folder of its own.</summary>
    [TestMethod]
    public async Task ShiftF5UnpacksWhatAltF5Packed()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Alt+F5");
        await _app.WaitForTextAsync("Compress readme.txt");
        await _app.PressAsync("Enter");
        var archive = Path.Combine(_app.RightRoot, "readme.zip");
        await _app.WaitUntilAsync(() => ZipEntries(archive).Contains("readme.txt"), "the archive to be written");
        await _app.WaitForNoDialogAsync();

        await _app.PressAsync("Tab");
        await _app.SelectEntryAsync("readme.zip");
        Assert.IsTrue(await _app.PressAsync("Shift+F5"), "Shift+F5 is taken over an archive");
        await _app.WaitForTextAsync("Extract readme.zip");
        await _app.WaitForTextAsync("Each archive into a folder of its own");
        await _app.PressAsync("Enter");

        var unpacked = Path.Combine(_app.LeftRoot, "readme", "readme.txt");
        var original = File.ReadAllText(Path.Combine(_app.LeftRoot, "readme.txt"));
        await _app.WaitUntilAsync(() => File.Exists(unpacked) && SharedFileReader.ReadAllText(unpacked) == original, "the file to come back out");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme", s), "the left panel to list the new folder");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task ShiftF5DoesNothingOnAFileThatIsNoArchive()
    {
        await _app.SelectEntryAsync("notes.md");

        Assert.IsFalse(await _app.PressAsync("Shift+F5"), "notes.md is not an archive");
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal);
    }

    [TestMethod]
    public async Task EscClosesTheDialogAndWritesNothing()
    {
        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("Alt+F5");
        await _app.WaitForTextAsync("Compress notes.md");

        await _app.PressAsync("Esc");

        await _app.WaitForNoDialogAsync();
        Assert.AreEqual(0, Directory.GetFiles(_app.RightRoot, "notes.*").Length);
        await _app.WaitForNoStrayFramesAsync();
    }
}
