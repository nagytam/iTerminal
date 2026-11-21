using iTerminal.Core.Dialogs;

namespace iTerminal.Test;

/// <summary>
/// Path completion from the folders on disk, in the running app: a destination typed in the Copy and Move dialogs is
/// completed from what exists (the sandbox lists only its own folders), and a dialog that opens on a folder does not
/// throw its list over the buttons before anything was typed.
/// </summary>
[TestClass]
public class DirectoryCompletionTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await (_app?.DisposeAsync() ?? ValueTask.CompletedTask);

    private string RightFolderText => _app.RightRoot + Path.DirectorySeparatorChar;

    [Timeout(120_000)]
    [TestMethod]
    public async Task TabCompletesAFolderTypedInTheCopyDialog()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("F5");
        CopyDialog dialog = await _app.Driver.WaitForModalAsync<CopyDialog>();

        dialog.TextBoxTarget.Text = RightFolderText;
        await _app.TypeAsync("tar");

        await WaitForBoxAsync(dialog.TextBoxTarget, () => dialog.TextBoxTarget.AutoCompletion!.IsPopupOpen, "the suggestion list to open under the box");
        await _app.PressAsync("Tab");

        await WaitForBoxAsync(dialog.TextBoxTarget, () => dialog.TextBoxTarget.Text == RightFolderText + "target" + Path.DirectorySeparatorChar, "Tab to replace the text with the folder");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TabCompletesAFolderTypedInTheMoveDialog()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Shift+F6");
        MoveDialog dialog = await _app.Driver.WaitForModalAsync<MoveDialog>();

        dialog.TextBoxTarget.Text = RightFolderText;
        await _app.TypeAsync("tar");

        await WaitForBoxAsync(dialog.TextBoxTarget, () => dialog.TextBoxTarget.AutoCompletion!.IsPopupOpen, "the suggestion list to open under the box");
        await _app.PressAsync("Tab");

        await WaitForBoxAsync(dialog.TextBoxTarget, () => dialog.TextBoxTarget.Text == RightFolderText + "target" + Path.DirectorySeparatorChar, "Tab to replace the text with the folder");
    }

    /// <summary>
    /// Waits as <see cref="TerminalApp.WaitUntilAsync(Func{bool}, string, TimeSpan?)"/> does, and on a timeout adds the
    /// state of the box and its suggestion list at that moment (popup open, highlighted item, matches, text), so a CI
    /// failure says which step went wrong without a rerun.
    /// </summary>
    private async Task WaitForBoxAsync(iConsole.Controls.TextBox box, Func<bool> condition, string because)
    {
        try
        {
            await _app.WaitUntilAsync(condition, because);
        }
        catch (AssertFailedException exception)
        {
            iConsole.Controls.AutoCompletion? completion = box.AutoCompletion;
            string state = $"Box state: popup open={completion?.IsPopupOpen}, highlighted='{completion?.GetSelectedValue()}', "
                + $"matches=[{string.Join(", ", completion?.CompletionModel.MatchingValues ?? [])}], text='{box.Text}'";
            throw new AssertFailedException($"{exception.Message}{Environment.NewLine}{state}", exception);
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ADialogOpenedOnAFolderDoesNotShowTheListUntilSomethingIsTyped()
    {
        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("Insert");
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Insert");
        await _app.PressAsync("F5");
        CopyDialog dialog = await _app.Driver.WaitForModalAsync<CopyDialog>();

        // Whatever the box was filled with on opening, the list waits for a keystroke. The delay is long enough for the
        // opening update (a background task, parked for the next idle pass) to have landed.
        await Task.Delay(1500);

        Assert.IsFalse(dialog.TextBoxTarget.AutoCompletion!.IsPopupOpen, "nothing was typed, so no list");
    }
}
