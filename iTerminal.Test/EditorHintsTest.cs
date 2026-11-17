namespace iTerminal.Test;

/// <summary>The text editor's title says how to leave it, at every width, as the file viewer's does.</summary>
[TestClass]
public class EditorHintsTest
{
    [Timeout(120_000)]
    [TestMethod]
    public async Task OnAnEightyColumnScreen_TheEditorTitleSaysHowToClose()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 80, height: 24);
        await app.SelectEntryAsync("readme.txt");

        await app.PressAsync("F4");

        var screen = await app.WaitForTextAsync("Esc:close");
        Assert.IsTrue(screen.Lines[0].Contains("Esc:close"), screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task OnAWideScreen_TheEditorTitleSaysHowToClose()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");

        await app.PressAsync("F4");

        var screen = await app.WaitForTextAsync("Esc:close");
        Assert.IsTrue(screen.Lines[0].Contains("Esc:close"), screen.Dump());
    }
}
