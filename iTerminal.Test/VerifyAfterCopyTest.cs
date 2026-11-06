namespace iTerminal.Test;

/// <summary>
/// The "Verify after copy" box of the copy dialog, end to end on the sandboxed app. The files are real
/// temporary ones, so this proves the box is wired, remembered and does not get in the way of a good copy;
/// that a damaged copy is caught is proved against a corrupting file system in the unit tests.
/// </summary>
[TestClass]
public class VerifyAfterCopyTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private static string? ReadIfFinished(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheBoxIsOffAtFirst_CanBeTicked_IsRemembered_AndAVerifiedCopyArrives()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Verify after copy");
        Assert.IsTrue(_app.Screen.Dump().Contains("□ Verify after copy"), "off by default");

        // Text box -> Overwrite -> Verify after copy.
        await _app.PressAsync("Tab");
        await _app.PressAsync("Tab");
        await _app.PressAsync("Space");
        await _app.WaitForTextAsync("▣ Verify after copy");
        await _app.PressAsync("Enter");

        string original = File.ReadAllText(Path.Combine(_app.LeftRoot, "readme.txt"));
        await _app.WaitUntilAsync(() => ReadIfFinished(Path.Combine(_app.RightRoot, "readme.txt")) == original, "readme.txt to be copied and verified");
        await _app.WaitForNoDialogAsync();

        await _app.SelectEntryAsync("notes.md");
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("▣ Verify after copy");
        await _app.PressAsync("Escape");
    }
}
