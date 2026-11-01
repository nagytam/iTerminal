using System.Text.RegularExpressions;

namespace iTerminal.Test;

/// <summary>
/// Ctrl+T, end to end: the focused panel lists its entries in several name columns, names only, and a second
/// Ctrl+T gives the one-column listing back.
/// </summary>
[TestClass]
public class BriefListingTest
{
    private static readonly string[] Names = ["alpha.txt", "beta.txt", "gamma.txt", "delta.txt", "epsilon.txt", "zeta.bin"];

    private TerminalApp _app = null!;

    /// <summary>True when one row of the panel carries at least two of the seeded names.</summary>
    private static bool HasTwoNamesOnARow(string row) => Names.Count(name => row.Contains(name)) >= 2;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlT_ListsTheFocusedPanelInColumns_AndBack()
    {
        _app = await TerminalApp.StartAsync(seed: (left, _) =>
        {
            foreach (var name in Names)
            {
                File.WriteAllText(Path.Combine(left, name), "x");
            }
        });
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("zeta.bin", s), "the seeded files to be listed");

        // One-column listing: one name per row.
        var longScreen = await _app.WaitUntilAsync(s => _app.LeftPanel(s).Any(row => row.Contains("alpha.txt")), "the Long listing");
        Assert.IsFalse(_app.LeftPanel(longScreen).Any(HasTwoNamesOnARow), "Long shows one name per row");

        await _app.PressAsync("Ctrl+T");

        // Brief: several names share a row, and no row carries a date.
        var briefScreen = await _app.WaitUntilAsync(
            s => _app.LeftPanel(s).Any(HasTwoNamesOnARow),
            "the Brief listing to put two names on one row");
        var rows = _app.LeftPanel(briefScreen);
        Assert.IsFalse(rows.Any(row => Regex.IsMatch(row, @"\d{4}-\d{2}-\d{2}")), "Brief shows names only" + Environment.NewLine + briefScreen.Dump());

        await _app.PressAsync("Ctrl+T");

        // Back in one column.
        var backScreen = await _app.WaitUntilAsync(
            s => !_app.LeftPanel(s).Any(HasTwoNamesOnARow),
            "the one-column listing to come back");
        Assert.IsTrue(_app.LeftPanel(backScreen).Any(row => row.Contains("alpha.txt")), backScreen.Dump());
        await _app.WaitForNoStrayFramesAsync();
    }
}
