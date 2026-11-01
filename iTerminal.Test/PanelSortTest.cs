using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;

namespace iTerminal.Test;

/// <summary>
/// Shift+F11, end to end: the panel's rows change order on screen, the cursor stays on its entry,
/// the footer says how the panel is sorted, and the choice outlives a change of drive and a restart.
/// </summary>
[TestClass]
public class PanelSortTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    /// <summary>
    /// The sandbox plus one file, with dates set: by name the files read notes.md, readme.txt,
    /// zeta.bin; by size zeta.bin (1), notes.md (7), readme.txt (40); by date readme.txt, zeta.bin, notes.md.
    /// </summary>
    private async Task StartAsync()
    {
        _app = await TerminalApp.StartAsync(seed: (left, _) =>
        {
            File.WriteAllText(Path.Combine(left, "zeta.bin"), "z");
            File.SetLastWriteTime(Path.Combine(left, "readme.txt"), new DateTime(2026, 1, 1, 10, 0, 0));
            File.SetLastWriteTime(Path.Combine(left, "zeta.bin"), new DateTime(2026, 2, 1, 10, 0, 0));
            File.SetLastWriteTime(Path.Combine(left, "notes.md"), new DateTime(2026, 3, 1, 10, 0, 0));
        });
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("zeta.bin", s), "the seeded file to be listed");
    }

    /// <summary>The rows the left panel lists the three files on, top to bottom.</summary>
    private string[] FileOrder(iConsole.Test.Core.Automation.ConsoleSnapshot screen) =>
        [.. new[] { "notes.md", "readme.txt", "zeta.bin" }.OrderBy(name => _app.LeftPanel(screen).ToList().FindIndex(row => row.Length > 1 && row[1..].StartsWith(name + " ", StringComparison.Ordinal)))];

    private Task WaitForOrderAsync(params string[] expected) =>
        _app.WaitUntilAsync(s => FileOrder(s).SequenceEqual(expected), "the files to read " + string.Join(", ", expected));

    private async Task SortAsync(params string[] keys)
    {
        await _app.PressAsync("Shift+F11");
        await _app.Driver.WaitForModalAsync<SortDialog>();
        foreach (var key in keys)
        {
            await _app.PressAsync(key);
        }
        await _app.WaitForNoDialogAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ShiftF11_SortsTheFocusedPanel_BySizeAndBack()
    {
        await StartAsync();
        await WaitForOrderAsync("notes.md", "readme.txt", "zeta.bin");

        // A mode's letter applies it at once.
        await SortAsync("s");
        await WaitForOrderAsync("zeta.bin", "notes.md", "readme.txt");
        var screen = await _app.WaitForTextAsync("Sort: size ↑");
        Assert.IsTrue(_app.LeftPanel(screen)[1].Contains(".."), "the way out stays first" + Environment.NewLine + screen.Dump());
        Assert.IsTrue(screen.RowOf("beta") < screen.RowOf("zeta.bin"), "folders stay ahead of files" + Environment.NewLine + screen.Dump());

        // r reverses and leaves the dialog open; Enter applies the mode under the cursor.
        await SortAsync("r", "Enter");
        await WaitForOrderAsync("readme.txt", "notes.md", "zeta.bin");
        await _app.WaitForTextAsync("Sort: size ↓");

        await SortAsync("a");
        await WaitForOrderAsync("notes.md", "readme.txt", "zeta.bin");
        await _app.WaitForTextGoneAsync("Sort:");
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ByDateAndByExtension()
    {
        await StartAsync();

        await SortAsync("d");
        await WaitForOrderAsync("readme.txt", "zeta.bin", "notes.md");

        await SortAsync("e");
        await WaitForOrderAsync("zeta.bin", "notes.md", "readme.txt");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCursorStaysOnItsEntry_AndTheOtherPanelIsLeftAlone()
    {
        await StartAsync();
        await _app.SelectEntryAsync("readme.txt");

        await SortAsync("s");
        await WaitForOrderAsync("zeta.bin", "notes.md", "readme.txt");

        Assert.AreEqual("readme.txt", _app.FocusedEntryName, "the cursor followed the file, not the row number");
        var right = _app.ContentModels.ContentModels.AsSnapshot().First(m => !ReferenceEquals(m, _app.FocusedModel));
        Assert.AreEqual(PanelSort.AsListed, ((ISortableContentModel)right).Sort);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheOrderIsThePanels_AcrossAChangeOfDrive_AndARestart()
    {
        await StartAsync();
        await SortAsync("s");
        await SortAsync("r", "Enter");
        await WaitForOrderAsync("readme.txt", "notes.md", "zeta.bin");

        // To the docker drive and back: a different model each way, the same order.
        await _app.PressAsync("Ctrl+F9");
        await _app.WaitUntilAsync(() => _app.FocusedModel?.Url.FullUrl.StartsWith("docker://") == true, "the docker drive");
        Assert.AreEqual(new PanelSort(PanelSortMode.Size, true), ((ISortableContentModel)_app.FocusedModel!).Sort);
        await _app.PressAsync("Ctrl+F9");
        await _app.WaitForFocusedFolderAsync(_app.LeftRoot);
        await WaitForOrderAsync("readme.txt", "notes.md", "zeta.bin");

        _app = await _app.RestartAsync();

        await WaitForOrderAsync("readme.txt", "notes.md", "zeta.bin");
        await _app.WaitForTextAsync("Sort: size ↓");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task MarksAndTheCursorActOnTheSortedRows()
    {
        await StartAsync();
        await SortAsync("s");
        await WaitForOrderAsync("zeta.bin", "notes.md", "readme.txt");

        // Home, then down past "..", alpha and beta: the first file by size is zeta.bin.
        await _app.PressAsync("Home");
        await _app.PressSequenceAsync("Down", "Down", "Down");
        Assert.AreEqual("zeta.bin", _app.FocusedEntryName);

        // Insert marks the row under the cursor and steps down — onto the next row as sorted.
        await _app.PressAsync("Insert");
        Assert.AreEqual("notes.md", _app.FocusedEntryName);
        await _app.WaitForTextAsync("1 marked");
    }
}
