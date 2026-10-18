namespace iTerminal.Test;

/// <summary>The workspace manager used the way a person uses it: save the screen, move on, switch back.</summary>
[TestClass]
public class WorkspacesTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task OpenManagerAsync()
    {
        Assert.IsTrue(await _app.PressAsync("Ctrl+Shift+F2"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.Workspaces.WorkspaceManagerDialog, "the workspace manager to open");
    }

    [TestMethod]
    public async Task TheManagerOpensOnAnEmptyListThatSaysWhatSavingDoes()
    {
        await OpenManagerAsync();

        await _app.WaitForTextAsync("No workspaces yet. S saves the screen as it is now");
        await _app.WaitForTextAsync("Enter switch  1-9 jump  S save  U update  R rename  D delete  Esc close");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }

    [TestMethod]
    public async Task ASavedWorkspaceBringsBackTheFolderOfThePanel()
    {
        await OpenManagerAsync();
        await _app.PressAsync("s");
        await _app.WaitForTextAsync("Save the screen as a workspace:");
        await _app.TypeAsync("Home");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Saved 'Home'.");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        await _app.SelectEntryAsync("alpha");
        await _app.PressAsync("Enter");
        await _app.WaitForFocusedFolderAsync(Path.Combine(_app.LeftRoot, "alpha"));

        await OpenManagerAsync();
        await _app.WaitForTextAsync("○ The screen has changed since.");
        await _app.PressAsync("1");

        await _app.WaitForNoDialogAsync();
        await _app.WaitForFocusedFolderAsync(_app.LeftRoot);
    }

    [TestMethod]
    public async Task TheManagerFitsAnEightyByTwentyFourScreen()
    {
        await using TerminalApp small = await TerminalApp.StartAsync(width: 80, height: 24);

        await small.PressAsync("Ctrl+Shift+F2");

        await small.WaitUntilAsync(
            s => s.Lines.Any(l => l.Contains("Workspaces", StringComparison.Ordinal) && l.TrimEnd().EndsWith('╗'))
                 && s.Lines.Any(l => l.Contains("Esc close", StringComparison.Ordinal)),
            "the dialog with its border and key hints inside the screen");
    }
}
