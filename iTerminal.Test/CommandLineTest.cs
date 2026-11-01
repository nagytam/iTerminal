namespace iTerminal.Test;

/// <summary>Copying names from a panel to the command line and walking its history, end to end.</summary>
[TestClass]
public class CommandLineTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AltEnter_PutsTheFileNameOnTheCommandLine_AndShowsTheLine()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Alt+Enter");

        var screen = await _app.WaitForTextAsync("Terminal>readme.txt");
        Assert.IsTrue(screen.Contains("readme.txt"), screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlEnter_AddsTheNextNameAfterABlank()
    {
        _app = await TerminalApp.StartAsync();
        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("Ctrl+Enter");
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+Enter");

        await _app.WaitForTextAsync("notes.md readme.txt");
    }
}
