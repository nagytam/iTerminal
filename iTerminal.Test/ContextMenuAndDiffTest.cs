namespace iTerminal.Test;

/// <summary>The right-click menu on a panel row and Ctrl+F10's file diff, through the whole app.</summary>
[TestClass]
public class ContextMenuAndDiffTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [Timeout(120_000)]
    [TestMethod]
    public async Task RightClickOnAFileOffersWhatCanBeDoneWithIt()
    {
        await _app.SelectEntryAsync("readme.txt");
        var screen = await _app.WaitForTextAsync("readme.txt");
        var position = screen.Find("readme.txt")!;

        await _app.Driver.RightClickAsync((int)position.X, (int)position.Y);

        screen = await _app.WaitForTextAsync("Properties");
        foreach (var item in new[] { "Open", "View", "Copy", "Delete", "Checksums", "Open with" })
        {
            Assert.IsTrue(screen.Contains(item), $"the menu offers {item}\n{screen.Dump()}");
        }

        await _app.PressAsync("Esc");
        await _app.WaitForTextGoneAsync("Checksums");
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ChoosingAnItemInTheMenuRunsThatCommandOnTheRow()
    {
        await _app.SelectEntryAsync("readme.txt");
        var screen = await _app.WaitForTextAsync("readme.txt");
        var position = screen.Find("readme.txt")!;
        await _app.Driver.RightClickAsync((int)position.X, (int)position.Y);

        await _app.Driver.ClickAsync("Properties");

        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.PropertiesDialog>();
        Assert.IsNotNull(dialog);
        await _app.WaitForTextAsync("readme.txt");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task RightClickOnTheParentRowOffersNothing()
    {
        await _app.Driver.WaitForTextAsync("..");
        var screen = await _app.WaitForTextAsync("..");
        var position = screen.Find("..")!;

        await _app.Driver.RightClickAsync((int)position.X, (int)position.Y);
        await Task.Delay(300);

        Assert.IsFalse((await _app.WaitForTextAsync("..")).Contains("Checksums"));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlF10ComparesTwoMarkedFiles()
    {
        File.WriteAllText(Path.Combine(_app.LeftRoot, "notes.md"), "one\ntwo\nthree\n");
        File.WriteAllText(Path.Combine(_app.LeftRoot, "readme.txt"), "one\n2\nthree\n");
        await _app.Driver.PressAsync("Ctrl+R");
        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("Insert");
        await _app.PressAsync("Insert");

        await _app.PressAsync("Ctrl+F10");

        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.FileDiffDialog>();
        await dialog.LoadTask;
        Assert.IsNotNull(dialog.Result, dialog.Status);
        Assert.AreEqual(1, dialog.Result.HunkStarts.Count);
        var screen = await _app.WaitForTextAsync("1 difference");
        Assert.IsTrue(screen.Contains("two") && screen.Contains("notes.md"), screen.Dump());

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlF10ComparesTheCursorFilesOfBothPanels()
    {
        File.WriteAllText(Path.Combine(_app.LeftRoot, "readme.txt"), "alpha\nbeta\n");
        File.WriteAllText(Path.Combine(_app.RightRoot, "right_only.txt"), "alpha\ngamma\n");
        await _app.Driver.PressAsync("Ctrl+R");
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Tab");
        await _app.Driver.WaitUntilAsync(() => _app.ContentModels.FocusedPanel == Core.Services.ContentModelService.Right, "the right panel to have focus");
        await _app.SelectEntryAsync("right_only.txt");
        await _app.PressAsync("Shift+Tab");
        await _app.Driver.WaitUntilAsync(() => _app.ContentModels.FocusedPanel == Core.Services.ContentModelService.Left, "the left panel to have focus");

        await _app.PressAsync("Ctrl+F10");

        var dialog = await _app.Driver.WaitForModalAsync<Core.Dialogs.FileDiffDialog>();
        await dialog.LoadTask;
        Assert.AreEqual(1, dialog.Result!.HunkStarts.Count);
        await _app.WaitForTextAsync("gamma");
    }
}
