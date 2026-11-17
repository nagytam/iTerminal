namespace iTerminal.Test;

/// <summary>
/// The file viewer's title says how to leave it, in both of its views: the text view and the hex dump.
/// </summary>
[TestClass]
public class ViewerHintsTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheTextAndHexViewsOfTheViewerBothSayHowToClose()
    {
        _app = await TerminalApp.StartAsync();
        // readme.txt is the fifth row: "..", alpha, beta, notes.md, readme.txt.
        await _app.PressSequenceAsync("Down", "Down", "Down", "Down");

        await _app.PressAsync("F3");
        var text = await _app.WaitForTextAsync("F8:Codepage");
        Assert.IsTrue(text.Contains("Esc:close"), text.Dump());

        await _app.PressAsync("F4");
        var hex = await _app.WaitForTextAsync("[HEX]");
        Assert.IsTrue(hex.Contains("Esc:close"), hex.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task OnAnEightyColumnScreen_TheTitleStillSaysHowToClose()
    {
        _app = await TerminalApp.StartAsync(width: 80, height: 24);
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F3");

        var screen = await _app.WaitForTextAsync("Esc:close");
        Assert.IsTrue(screen.Lines[0].Contains("Esc:close"), screen.Dump());
    }
}
