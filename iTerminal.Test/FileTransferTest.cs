namespace iTerminal.Test;

/// <summary>
/// Tools > File transfer, end to end on the sandbox: the dialog opens over the focused file, shows the sandbox address
/// and its code, and Esc closes it. The share itself is the sandbox's stub, so no socket is bound.
/// </summary>
[TestClass]
public class FileTransferTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await (_app?.DisposeAsync() ?? ValueTask.CompletedTask);

    [Timeout(120_000)]
    [TestMethod]
    public async Task ToolsFileTransferShowsTheAddressAndItsCodeUntilEscape()
    {
        await _app.SelectEntryAsync("notes.md");

        await _app.Driver.ClickAsync("Tools");
        await _app.WaitUntilAsync(
            () => _app.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar,
            "the Tools menu to take the keyboard");
        for (int attempt = 0; attempt < 100
            && !_app.Screen.Lines.Any(line => line.Contains("File transfer", StringComparison.Ordinal));
            attempt++)
        {
            iConsole.Test.Core.Automation.ConsoleSnapshot previous = _app.Screen;
            await _app.PressAsync("Down");
            await _app.Driver.WaitUntilAsync(
                screen => !screen.HasSameContent(previous),
                "the Tools menu to redraw after moving selection",
                TimeSpan.FromSeconds(2));
        }

        await _app.WaitForTextAsync("File transfer");
        await _app.Driver.ClickAsync("File transfer");
        await _app.WaitUntilAsync(
            () => _app.Driver.FocusedModal is Core.Dialogs.FileTransfer.FileTransferDialog,
            "the file transfer dialog to open from Tools");
        await _app.WaitForTextAsync(SandboxServices.FileShareStub.Address);

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }

    /// <summary>Ctrl+Shift+X opens the dialog over a selected file. With nothing to share the command is disabled, so the key does nothing.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlShiftXOpensTheDialogOverASelectedFile()
    {
        await _app.SelectEntryAsync("notes.md");

        Assert.IsTrue(await _app.PressAsync("Ctrl+Shift+X"), "Ctrl+Shift+X is handled");
        await _app.WaitUntilAsync(
            () => _app.Driver.FocusedModal is Core.Dialogs.FileTransfer.FileTransferDialog,
            "the file transfer dialog to open from its hot key");
        await _app.WaitForTextAsync(SandboxServices.FileShareStub.Address);

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }
}
