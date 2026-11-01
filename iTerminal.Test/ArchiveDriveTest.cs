using iTerminal.Core.Services.Archives;

namespace iTerminal.Test;

/// <summary>An archive opened with Enter, browsed like a folder, copied out of with F5, and left with "..".</summary>
[TestClass]
public class ArchiveDriveTest
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

    /// <summary>bundle.tar.gz in the left folder: a.txt and docs/b.md.</summary>
    private static void Seed(string left, string right)
    {
        var staging = Path.Combine(Path.GetTempPath(), "ArchiveDriveTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(staging, "docs"));
        File.WriteAllText(Path.Combine(staging, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(staging, "docs", "b.md"), "beta from the archive");
        ManagedArchiveWriter.WriteAsync(
            Path.Combine(left, "bundle.tar.gz"),
            [
                new ArchiveItem("a.txt", false, 5, DateTime.Now, Path.Combine(staging, "a.txt")),
                new ArchiveItem("docs/", true, 0, DateTime.Now, Path.Combine(staging, "docs")),
                new ArchiveItem("docs/b.md", false, 21, DateTime.Now, Path.Combine(staging, "docs", "b.md")),
            ],
            new CompressionOptions(ArchiveFormat.TarGz, 6), null, CancellationToken.None).GetAwaiter().GetResult();
        Directory.Delete(staging, recursive: true);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EnterOpensATarGzAndF5CopiesOutOfIt()
    {
        _app = await TerminalApp.StartAsync(Seed);

        await _app.SelectEntryAsync("bundle.tar.gz");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("docs", s) && _app.LeftPanelLists("a.txt", s), "the archive's top level");
        await _app.WaitForTextAsync("bundle.tar.gz:/");

        await _app.SelectEntryAsync("docs");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("b.md", s), "the folder inside the archive");

        await _app.SelectEntryAsync("b.md");
        await _app.PressAsync("F5");
        await _app.WaitForTextAsync("Copy");
        await _app.PressAsync("Enter");
        var copy = Path.Combine(_app.RightRoot, "b.md");
        await _app.WaitUntilAsync(() => File.Exists(copy) && SharedFileReader.ReadAllText(copy) == "beta from the archive", "the entry to be copied out");
        await _app.WaitForNoDialogAsync();

        // ".." up to the archive's top, ".." again out of it: back in the folder, on the archive.
        await _app.PressAsync("Home");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("a.txt", s), "the archive's top level again");
        await _app.PressAsync("Home");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s), "the folder the archive is in");
        await _app.WaitUntilAsync(() => _app.FocusedEntryName == "bundle.tar.gz", "the cursor to be on the archive");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F8IsNotOfferedInsideAnArchive()
    {
        _app = await TerminalApp.StartAsync(Seed);
        await _app.SelectEntryAsync("bundle.tar.gz");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("a.txt", s), "the archive's top level");

        await _app.SelectEntryAsync("a.txt");
        await _app.PressAsync("F8");
        await _app.WaitForTextAsync("Delete file");
        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("read-only");
    }
}
