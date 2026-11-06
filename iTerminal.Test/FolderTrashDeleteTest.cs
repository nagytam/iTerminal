namespace iTerminal.Test;

/// <summary>
/// F8 on a local folder tree with "Move to Trash" left ticked: the whole tree goes to the Trash as one
/// entry, not file by file. Linux only: the sandbox Trash is the freedesktop one.
/// </summary>
[TestClass]
public class FolderTrashDeleteTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync(seed: (left, _) =>
    {
        var tree = Path.Combine(left, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub", "deeper"));
        File.WriteAllText(Path.Combine(tree, "top.txt"), "top");
        File.WriteAllText(Path.Combine(tree, "sub", "nested.txt"), "nested");
        File.WriteAllText(Path.Combine(tree, "sub", "deeper", "deep.txt"), "deep");
    });

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [Timeout(120_000)]
    [TestMethod]
    public async Task F8OnALocalFolderTreeTakesTheWholeTreeToTheTrashAsOneEntry()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The sandbox Trash is the freedesktop one, on Linux.");
        }

        await _app.SelectEntryAsync("tree");

        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("▣ Move to Trash");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(() => !Directory.Exists(Path.Combine(_app.LeftRoot, "tree")), "the folder tree to leave the disk");
        await _app.WaitUntilAsync(s => !_app.LeftPanelLists("tree", s), "the panel to drop it");
        await _app.WaitForNoDialogAsync();

        var files = Path.Combine(_app.TrashFolder, "files");
        var entries = Directory.GetFileSystemEntries(files).Select(Path.GetFileName).ToArray();
        CollectionAssert.AreEqual(new[] { "tree" }, entries, "the tree is one entry in the Trash");
        Assert.IsTrue(File.Exists(Path.Combine(files, "tree", "sub", "deeper", "deep.txt")), "its contents are kept in that entry");
    }
}
