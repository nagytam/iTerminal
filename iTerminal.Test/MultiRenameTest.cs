namespace iTerminal.Test;

/// <summary>
/// Multi-rename from the Edit menu, end to end: mark two rows, choose Edit > Multi-rename, type a
/// pattern, and Enter renames exactly the marked files on disk. It is reached from the menu only (no
/// chord), so the test goes through the menu bar as a user does.
/// </summary>
[TestClass]
public class MultiRenameTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [TestMethod]
    public async Task EditMenuRenamesTheMarkedFilesByThePattern()
    {
        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("Insert");
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Insert");
        await _app.WaitForTextAsync("2 marked");

        await _app.Driver.ClickAsync("Edit");
        await _app.WaitForTextAsync("Multi-rename");
        await _app.Driver.ClickAsync("Multi-rename");
        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.MultiRenameDialog>();
        Assert.IsNotNull(dialog, "the Multi-rename dialog opened");

        // The pattern starts as [C] (rename nothing): replace it with [C]_x.
        await _app.PressSequenceAsync("Backspace", "Backspace", "Backspace");
        await _app.TypeAsync("[C]_x");
        await _app.WaitForTextAsync("readme_x.txt");
        await _app.WaitForTextAsync("notes_x.md");

        await _app.PressAsync("Enter");

        var left = _app.LeftRoot;
        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(left, "readme_x.txt")) && File.Exists(Path.Combine(left, "notes_x.md")),
            "both marked files to be renamed on disk");
        Assert.IsFalse(File.Exists(Path.Combine(left, "readme.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(left, "notes.md")));
        Assert.IsTrue(File.Exists(Path.Combine(left, "alpha", "inside_alpha.txt")), "a folder that was not marked is untouched");
        await _app.WaitForTextGoneAsync("2 marked");
    }
}
