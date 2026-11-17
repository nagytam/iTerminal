namespace iTerminal.Test;

/// <summary>The environment variables dialog keeps its close key on its footer on a narrow screen.</summary>
[TestClass]
public class EnvVarsFooterTest
{
    [Timeout(120_000)]
    [TestMethod]
    public async Task On60Columns_TheFooterStillSaysEscClose()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 60, height: 20);

        await app.PressAsync("Ctrl+Shift+H");

        var screen = await app.WaitForTextAsync("Environment Variables");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Esc:close")), "the footer's close key on screen");
        Assert.IsTrue(screen.Lines.Any(l => l.Contains("N new")), screen.Dump());
    }
}
