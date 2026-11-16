using System.IO;

namespace iTerminal.Test;

/// <summary>
/// Ctrl+Shift+S on screen: a hidden file is listed while hidden files are shown, and gone from both panels when they
/// are not. The panels' listing is what the user sees, so the check reads the rows, not the stored setting.
/// </summary>
[TestClass]
public class HiddenFilesTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync(seed: (left, _) =>
    {
        var path = Path.Combine(left, "secret.txt");
        File.WriteAllText(path, "hidden");
        File.SetAttributes(path, FileAttributes.Hidden);
    });

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlShiftSHidesAndShowsTheHiddenFileRows()
    {
        await _app.WaitUntilAsync(s => s.Contains("secret.txt"), "the hidden file to be listed while hidden files are shown");

        Assert.IsTrue(await _app.PressAsync("Ctrl+Shift+S"));
        await _app.WaitUntilAsync(s => !s.Contains("secret.txt"), "the hidden file to leave both panels");

        Assert.IsTrue(await _app.PressAsync("Ctrl+Shift+S"));
        await _app.WaitUntilAsync(s => s.Contains("secret.txt"), "the hidden file to come back");
    }
}
