namespace iTerminal.Test;

/// <summary>
/// The user's key map, end to end: <c>keymap.json</c> in the data folder moves Compare folders from Shift+F2 to
/// Alt+Shift+F2 and the old key stops working. The refused binding in the same file (Ctrl+M) is covered by the unit tests.
/// </summary>
[TestClass]
public class KeyMapTest
{
    private const string KeyMapJson = """
        {
          "bindings": {
            "CompareFoldersDialogCommand": "Alt+Shift+F2",
            "CopyCommand": "Ctrl+M"
          }
        }
        """;

    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync(keyMapJson: KeyMapJson);

    [TestCleanup]
    public async Task StopAsync() => await (_app?.DisposeAsync() ?? ValueTask.CompletedTask);

    [Timeout(120_000)]
    [TestMethod]
    public async Task ARemappedKeyOpensTheCommandAndTheOldKeyDoesNot()
    {
        await _app.PressAsync("Shift+F2");
        await _app.WaitForNoDialogAsync();

        await _app.PressAsync("Alt+Shift+F2");
        await _app.Driver.WaitForModalAsync<Core.Dialogs.CompareFoldersDialog>();
    }
}
