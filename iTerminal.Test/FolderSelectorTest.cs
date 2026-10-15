using iTerminal.Core.Dialogs;

namespace iTerminal.Test;

/// <summary>Ctrl+F1, the folder selector, driven the way a user drives it: keys into the running app.</summary>
[TestClass]
public class FolderSelectorTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task<FolderSelectorDialog> OpenAsync()
    {
        Assert.IsTrue(await _app.PressAsync("Ctrl+F1"), "Ctrl+F1 is handled");
        return await _app.Driver.WaitForModalAsync<FolderSelectorDialog>();
    }

    private static int FocusedPageIndex(FolderSelectorDialog dialog) =>
        dialog.TabPages.ToList().FindIndex(page => page.IsFocused);

    [TestMethod]
    public async Task CtrlDownAndCtrlUp_MoveBetweenPages()
    {
        var dialog = await OpenAsync();
        Assert.AreEqual(0, FocusedPageIndex(dialog), "the selector opens on its first page");

        await _app.PressAsync("Ctrl+Down");
        await _app.WaitUntilAsync(() => FocusedPageIndex(dialog) == 1, "Ctrl+Down to reach the Drives page");

        await _app.PressAsync("Ctrl+Down");
        await _app.WaitUntilAsync(() => FocusedPageIndex(dialog) == 2, "a second Ctrl+Down to reach the History page");

        await _app.PressAsync("Ctrl+Up");
        await _app.WaitUntilAsync(() => FocusedPageIndex(dialog) == 1, "Ctrl+Up to go back to the Drives page");

        await _app.PressAsync("Ctrl+Up");
        await _app.WaitUntilAsync(() => FocusedPageIndex(dialog) == 0, "Ctrl+Up to return to the Favourites page");
    }

    [TestMethod]
    public async Task CtrlUpAndCtrlDown_AreNotSwallowedByThePageList()
    {
        var dialog = await OpenAsync();

        // The page's own list has focus on arrival; a plain Down goes to it, a Ctrl+Down to the strip.
        await _app.PressAsync("Down");
        await _app.PressAsync("Ctrl+Down");
        await _app.WaitUntilAsync(() => FocusedPageIndex(dialog) == 1, "Ctrl+Down from inside a page's list");
    }
}
