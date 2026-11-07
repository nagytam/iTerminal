using iTerminal.Core.Dialogs;

namespace iTerminal.Test;

/// <summary>Shift+Alt+F3, end to end: create a manifest of the sandbox folder, then catch a change.</summary>
[TestClass]
public class HashManifestTest
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
    public async Task ShiftAltF3_CreatesAManifest_AndVerifyFindsAChange()
    {
        _app = await TerminalApp.StartAsync();
        string manifest = Path.Combine(_app.LeftRoot, "SHA256SUMS");

        Assert.IsTrue(await _app.PressAsync("Shift+Alt+F3"));
        HashManifestDialog dialog = await _app.Driver.WaitForModalAsync<HashManifestDialog>();
        await _app.WaitForTextAsync("Covers: the whole folder tree");

        await _app.PressAsync("F2");
        await dialog.RunTask;
        await _app.WaitForTextAsync("Saved SHA256SUMS");
        string text = File.ReadAllText(manifest);
        StringAssert.Contains(text, " *readme.txt\n");
        StringAssert.Contains(text, " *alpha/inside_alpha.txt\n");

        File.AppendAllText(Path.Combine(_app.LeftRoot, "readme.txt"), "tampered");
        await _app.PressAsync("F3");
        await dialog.RunTask;
        await _app.WaitForTextAsync("1 changed");
        await _app.WaitForTextAsync("changed     readme.txt");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }
}
