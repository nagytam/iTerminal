using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using iTerminal.Core.Services;

namespace iTerminal.Test;

/// <summary>
/// The Panels page of the settings dialog (Shift+F8, Ctrl+6), end to end: a change on the page is
/// the setting — the panels behind the dialog re-list at once, and the next launch starts that way.
/// </summary>
[TestClass]
public class PanelSettingsTest
{
    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    /// <summary>The sandbox plus a hidden file and two numbered ones, in both panels.</summary>
    private async Task StartAsync()
    {
        _app = await TerminalApp.StartAsync(seed: (left, right) =>
        {
            foreach (var folder in new[] { left, right })
            {
                File.WriteAllText(Path.Combine(folder, "file2.txt"), "22");
                File.WriteAllText(Path.Combine(folder, "file10.txt"), "1");
                var hidden = Path.Combine(folder, ".hidden.txt");
                File.WriteAllText(hidden, "h");
                File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
            }
        });
        await _app.WaitUntilAsync(s => _app.LeftPanelLists(".hidden.txt", s) && _app.RightPanelLists(".hidden.txt", s), "the hidden file to be listed: that is the default");
    }

    private async Task<SettingsPanelsTabPage> OpenPanelsPageAsync()
    {
        await _app.PressAsync("Shift+F8");
        var dialog = await _app.Driver.WaitForModalAsync<SettingsDialog>();
        await _app.PressAsync("Ctrl+6");
        await _app.WaitForTextAsync("Left panel order");
        var tab = dialog.Controls.AsSnapshot().OfType<iConsole.Controls.Tabs.Tab>().Single();
        return tab.TabPages.OfType<SettingsPanelsTabPage>().Single();
    }

    private int RowOfLeft(string name, iConsole.Test.Core.Automation.ConsoleSnapshot screen) =>
        _app.LeftPanel(screen).ToList().FindIndex(row => row.Length > 1 && row[1..].StartsWith(name + " ", StringComparison.Ordinal));

    [TestMethod]
    public async Task ThePage_ShowsWhatIsSaved_IncludingWhatShiftF11Set()
    {
        await StartAsync();
        await _app.PressAsync("Shift+F11");
        await _app.Driver.WaitForModalAsync<SortDialog>();
        await _app.PressSequenceAsync("r", "s");
        await _app.WaitForNoDialogAsync();

        var page = await OpenPanelsPageAsync();

        Assert.AreEqual(new PanelSort(PanelSortMode.Size, true), page.LeftSort);
        Assert.AreEqual(PanelSort.AsListed, page.RightSort);
        Assert.AreEqual(PanelViewOptions.Default, page.ViewOptions);
        var screen = _app.Screen;
        Assert.IsTrue(screen.Contains("Show hidden files and folders"), screen.Dump());
        Assert.IsTrue(screen.Contains("file2 before file10"), screen.Dump());
    }

    [TestMethod]
    public async Task UntickingShowHidden_TakesHiddenFilesOutOfBothPanels_AtOnce_AndForTheNextLaunch()
    {
        await StartAsync();
        await OpenPanelsPageAsync();

        // The cursor opens on the left order; Tab past its Reversed box, the right order and its box.
        await _app.PressSequenceAsync("Tab", "Tab", "Tab", "Tab");
        await _app.PressAsync("Space");

        Assert.IsFalse(PanelViewOptions.Load(_app.Driver.GetRequiredService<ISettingsService>()).ShowHidden);
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitUntilAsync(
            s => !_app.LeftPanelLists(".hidden.txt", s) && !_app.RightPanelLists(".hidden.txt", s) && _app.LeftPanelLists("file2.txt", s),
            "the hidden file to leave both panels");
        Assert.IsTrue(File.Exists(Path.Combine(_app.LeftRoot, ".hidden.txt")), "not listed is not deleted");

        _app = await _app.RestartAsync();

        await _app.WaitUntilAsync(s => _app.LeftPanelLists("file2.txt", s), "the panels to be listed");
        Assert.IsFalse(_app.LeftPanelLists(".hidden.txt"), _app.Screen.Dump());
    }

    [TestMethod]
    public async Task TheLeftOrder_IsChosenFromTheList_AndOnlyTheLeftPanelChanges()
    {
        await StartAsync();
        var page = await OpenPanelsPageAsync();

        // The order box has the cursor: Up from "As listed" walks the list — Date, then Size.
        await _app.PressSequenceAsync("Up", "Up");
        Assert.AreEqual(new PanelSort(PanelSortMode.Size), page.LeftSort);
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        // By size: file10.txt (1 byte) before file2.txt (2 bytes).
        await _app.WaitUntilAsync(s => RowOfLeft("file10.txt", s) < RowOfLeft("file2.txt", s) && RowOfLeft("file10.txt", s) > 0, "the left panel by size");
        await _app.WaitForTextAsync("Sort: size ↑");
        var models = _app.ContentModels.ContentModels.AsSnapshot();
        Assert.AreEqual(PanelSort.AsListed, ((ISortableContentModel)models.Single(m => m.Name == ContentModelService.Right)).Sort);
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task NumbersInNames_SortByValue_UnlessThePageSaysOtherwise()
    {
        await StartAsync();
        await _app.PressAsync("Shift+F11");
        await _app.Driver.WaitForModalAsync<SortDialog>();
        await _app.PressAsync("n");
        await _app.WaitForNoDialogAsync();
        await _app.WaitUntilAsync(s => RowOfLeft("file2.txt", s) > 0 && RowOfLeft("file2.txt", s) < RowOfLeft("file10.txt", s), "file2 before file10");

        // Left order, its box, right order, its box, hidden, folders first — and the numbers box.
        await OpenPanelsPageAsync();
        await _app.PressSequenceAsync("Tab", "Tab", "Tab", "Tab", "Tab", "Tab");
        await _app.PressAsync("Space");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        await _app.WaitUntilAsync(s => RowOfLeft("file10.txt", s) > 0 && RowOfLeft("file10.txt", s) < RowOfLeft("file2.txt", s), "character by character, file10 before file2");
    }
}
