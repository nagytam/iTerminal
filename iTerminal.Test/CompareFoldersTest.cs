namespace iTerminal.Test;

/// <summary>Shift+F2, end to end: compare the two panels, see the differences, and sync with F5.</summary>
[TestClass]
public class CompareFoldersTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [Timeout(120_000)]
    [TestMethod]
    public async Task ShiftF2ListsTheDifferencesAndMarksWhatToCopyAcross()
    {
        await _app.PressAsync("Shift+F2");
        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.CompareFoldersDialog>();
        await _app.WaitForTextAsync("Enter:compare");

        await _app.PressAsync("Enter");
        await dialog.CompareTask;

        // Left: alpha/, beta/, notes.md, readme.txt. Right: target/, right_only.txt. Nothing is shared.
        var screen = await _app.WaitForTextAsync("only on the left");
        Assert.IsTrue(screen.Contains("◀ only left") && screen.Contains("only right ▶"), screen.Dump());
        Assert.AreEqual((4, 2), dialog.Marked);
        StringAssert.Contains(dialog.Status, "F5 here synchronises");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextAsync("4 marked");
        await _app.WaitForTextAsync("2 marked");
        await _app.WaitForNoStrayFramesAsync();

        // The left panel's marks are the sync: F5 copies them to the right.
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy 4 items");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "readme.txt"))
                                        && Directory.Exists(Path.Combine(_app.RightRoot, "alpha")), "the left side's files to arrive");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F5InTheDialogSynchronisesBothWaysAfterAConfirmation()
    {
        await _app.PressAsync("Shift+F2");
        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.CompareFoldersDialog>();
        await _app.WaitForTextAsync("F5:synchronise");

        // Nothing has been compared yet: F5 says so and changes nothing.
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Compare first");

        await _app.PressAsync("Enter");
        await dialog.CompareTask;

        // The first F5 only says what it would do; any other key withdraws it.
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("F5 again to do it");
        Assert.IsNotNull(dialog.PendingPlan);
        Assert.IsFalse(File.Exists(Path.Combine(_app.RightRoot, "readme.txt")));
        await _app.PressAsync("Down");
        await _app.WaitForTextAsync("nothing was changed");
        Assert.IsNull(dialog.PendingPlan);

        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("F5 again to do it");
        await _app.PressAsync("F5");
        await _app.WaitForNoDialogAsync();

        await _app.WaitUntilAsync(() => File.Exists(Path.Combine(_app.RightRoot, "readme.txt"))
                                        && Directory.Exists(Path.Combine(_app.RightRoot, "alpha"))
                                        && File.Exists(Path.Combine(_app.LeftRoot, "right_only.txt")), "both sides to receive what the other had");
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task IdenticalFoldersSaySo()
    {
        // The right panel shown the left panel's folder: everything is the same.
        await _app.PressAsync("Tab");
        await _app.Driver.WaitUntilAsync(() => _app.ContentModels.FocusedPanel == Core.Services.ContentModelService.Right, "the right panel to have focus");
        await _app.ContentModels.SetUrlAsync(Core.Services.ContentModelService.Right, new iConsole.Data.Url(_app.LeftRoot + Path.DirectorySeparatorChar));

        await _app.PressAsync("Shift+F2");
        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.CompareFoldersDialog>();
        await _app.PressAsync("Enter");
        await dialog.CompareTask;

        Assert.AreEqual(0, dialog.Result!.Differences.Count(), string.Join(", ", dialog.Result.Differences.Select(d => d.Path)));
        StringAssert.Contains(dialog.Status, "identical");
        Assert.AreEqual((0, 0), dialog.Marked);
    }
}
