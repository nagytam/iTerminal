namespace iTerminal.Test;

/// <summary>A file key with nothing to act on says why it did nothing, rather than leaving the screen unchanged.</summary>
[TestClass]
public class NothingToActOnTest
{
    [Timeout(120_000)]
    [TestMethod]
    public async Task CopyOnParentRow_SaysNothingIsSelectedAndOpensNoDialog()
    {
        await using TerminalApp app = await TerminalApp.StartAsync();
        // The cursor starts on the ".." row, which is never copied.

        await app.PressAsync("F5");

        await app.WaitForTextAsync("Nothing selected");
        await app.WaitForNoDialogAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task DeleteOnParentRow_SaysNothingIsSelectedAndAsksNothing()
    {
        await using TerminalApp app = await TerminalApp.StartAsync();

        await app.PressAsync("F8");

        await app.WaitForTextAsync("Nothing selected");
        await app.WaitForNoDialogAsync();
    }
}
