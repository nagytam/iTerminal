namespace iTerminal.Test;

/// <summary>The Edit menu names the file copy apart from the clipboard copy, which sits in the same menu.</summary>
[TestClass]
public class EditMenuLabelsTest
{
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheFileCopyIsNamedCopyFilesNotCopy()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 120, height: 36);

        await app.Driver.ClickAsync("Edit");

        var screen = await app.WaitForTextAsync("Copy Files");
        Assert.IsTrue(screen.Lines.Any(l => l.Contains("Copy to Clipboard")), screen.Dump());
        Assert.IsFalse(screen.Lines.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\bCopy\s+F5\b")), screen.Dump());
    }
}
