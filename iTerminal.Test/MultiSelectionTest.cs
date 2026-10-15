using iConsole.Display;

namespace iTerminal.Test;

/// <summary>
/// Marking several rows and acting on them at once, end to end: Insert, Shift+arrows and the
/// keypad's +/-/* mark; F5, Shift+F6 and F8 then act on every marked row; the marks are consumed.
/// </summary>
[TestClass]
public class MultiSelectionTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

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

    /// <summary>The colour the left panel draws <paramref name="name"/>'s first letter in.</summary>
    private Color LeftRowColor(string name)
    {
        var screen = _app.Screen;
        for (var y = 0; y < screen.Height; y++)
        {
            var line = screen.Line(y);
            var x = line.IndexOf(name + " ", StringComparison.Ordinal);
            if (x >= 0 && x < screen.Width / 2)
            {
                return screen.ForegroundAt(x, y);
            }
        }

        Assert.Fail($"'{name}' is not on screen:{Environment.NewLine}{screen.Dump()}");
        return default;
    }

    [TestMethod]
    public async Task InsertMarksRowsAndF5CopiesThemAll()
    {
        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("Insert");
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Insert");

        await _app.WaitForTextAsync("2 marked");
        Assert.AreEqual(Color.Yellow, LeftRowColor("notes.md"), "a marked row is drawn in yellow");

        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy 2 items");
        await _app.PressAsync("Enter");

        foreach (var name in new[] { "notes.md", "readme.txt" })
        {
            var original = File.ReadAllText(Path.Combine(_app.LeftRoot, name));
            await _app.WaitUntilAsync(() => ReadIfFinished(Path.Combine(_app.RightRoot, name)) == original, $"{name} to be copied");
        }

        await _app.WaitUntilAsync(s => _app.RightPanelLists("notes.md", s) && _app.RightPanelLists("readme.txt", s),
            "the target panel to list both copies");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextGoneAsync("2 marked");
    }

    [TestMethod]
    public async Task ShiftDownMarksAFolderAndAFileAndShiftF6MovesThem()
    {
        await _app.SelectEntryAsync("beta");
        await _app.PressAsync("Shift+Down");
        await _app.PressAsync("Shift+Down");
        await _app.WaitForTextAsync("2 marked");

        await _app.PressAsync("Shift+F6");
        await _app.WaitForTextAsync("Move 2 items");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => Directory.Exists(Path.Combine(_app.RightRoot, "beta")), "the folder to arrive");
        await _app.WaitUntilAsync(() => !Directory.Exists(Path.Combine(_app.LeftRoot, "beta")), "the folder to leave");
        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "notes.md")), "the file to arrive");
        Assert.IsFalse(File.Exists(Path.Combine(_app.LeftRoot, "notes.md")), "and to leave");
        Assert.IsTrue(Directory.Exists(Path.Combine(_app.LeftRoot, "alpha")), "the row above was not marked");
    }

    [TestMethod]
    public async Task NumPlusMarksByPatternAndF8DeletesTheMarkedFiles()
    {
        await _app.PressAsync("NumPlus");
        await _app.WaitForTextAsync("Mark files");
        await _app.PressAsync("End");
        await _app.PressAsync("Backspace"); // the "*" the box offers
        await _app.TypeAsync("*.md;*.txt");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("2 marked");

        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("Delete 2 items");
        await _app.PressAsync("Tab");      // onto "Move to Recycle bin"
        await _app.PressAsync("Space");    // untick it: the test must not fill the user's Recycle Bin
        await _app.WaitForTextAsync("□ Move to Recycle bin");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => Directory.GetFiles(_app.LeftRoot).Length == 0, "both files to be deleted");
        Assert.IsTrue(Directory.Exists(Path.Combine(_app.LeftRoot, "alpha")), "folders were not marked");
        await _app.WaitUntilAsync(s => !_app.LeftPanelLists("readme.txt", s) && !_app.LeftPanelLists("notes.md", s), "the panel to drop them");
    }

    [TestMethod]
    public async Task NumStarInvertsAndLeavingTheFolderDropsTheMarks()
    {
        await _app.PressAsync("NumStar");
        await _app.WaitForTextAsync("2 marked"); // the two files, not the folders

        await _app.SelectEntryAsync("alpha");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("inside_alpha.txt", s), "alpha to open");
        await _app.PressAsync("Ctrl+PageUp");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s), "the way back");

        await _app.WaitForTextGoneAsync("marked");
    }
}
