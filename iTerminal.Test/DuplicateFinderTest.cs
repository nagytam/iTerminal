namespace iTerminal.Test;

/// <summary>Ctrl+Shift+F7, end to end: the whole app finds the copies under the panel's folder and marks the extra ones.</summary>
[TestClass]
public class DuplicateFinderTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync()
    {
        _app = await TerminalApp.StartAsync();

        // The left folder holds readme.txt; put a copy of it in a sub-folder, a day older.
        var copy = Path.Combine(_app.LeftRoot, "alpha", "readme copy.txt");
        File.WriteAllText(copy, File.ReadAllText(Path.Combine(_app.LeftRoot, "readme.txt")));
        File.SetLastWriteTime(copy, File.GetLastWriteTime(Path.Combine(_app.LeftRoot, "readme.txt")).AddDays(-1));
    }

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [TestMethod]
    public async Task CtrlShiftF7ListsTheCopiesAndAMarksAllButTheOldest()
    {
        await _app.PressAsync("Ctrl+Shift+F7");
        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.DuplicateFinderDialog>();
        await dialog.Scan!.Completion;

        var screen = await _app.WaitForTextAsync("wasted");
        Assert.IsTrue(screen.Contains("readme copy.txt") && screen.Contains("readme.txt"), screen.Dump());
        Assert.AreEqual(1, dialog.Groups.Count);

        await _app.PressAsync("a");
        await _app.WaitForTextAsync("keeping the oldest");
        Assert.AreEqual(1, dialog.MarkedPaths.Count);
        StringAssert.EndsWith(dialog.MarkedPaths.Single(), "readme.txt", "the older copy in alpha/ is the one kept");
        Assert.IsFalse(dialog.MarkedPaths.Single().Contains("readme copy"));

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }
}
