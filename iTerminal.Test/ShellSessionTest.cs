namespace iTerminal.Test;

/// <summary>
/// The command log's shell line, end to end: a bare "shell" lists the shells, "shell bash" switches, and a command
/// runs in the session. The session is the sandbox's fake, so no shell process starts.
/// </summary>
[TestClass]
public class ShellSessionTest
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
    public async Task ShellLine_ListsTheShells_SwitchesThem_AndCommandsRunInTheSession()
    {
        _app = await TerminalApp.StartAsync();
        await _app.PressAsync("Ctrl+Shift+O");

        await _app.TypeAsync("shell");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Shells: sh, bash. Current: sh.");

        await _app.TypeAsync("shell bash");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Shell is now bash.");

        await _app.TypeAsync("ls");
        await _app.PressAsync("Enter");
        var screen = await _app.WaitForTextAsync("sandbox: ls");
        Assert.IsTrue(screen.Contains("sandbox: ls"), screen.Dump());
    }
}
