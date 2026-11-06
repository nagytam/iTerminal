using System.Text.RegularExpressions;

namespace iTerminal.Test;

/// <summary>
/// The Long and Info listings, end to end, through the command palette. Long shows each entry's attributes, date
/// and time with no size column. Info, on the right panel, shows the left panel's cursor entry and is read-only;
/// Esc gives the list back.
/// </summary>
[TestClass]
public class LongAndInfoListingTest
{
    private const string Target = "info-target.txt";

    private TerminalApp _app = null!;

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task RunPaletteCommandAsync(string name)
    {
        await _app.PressAsync("Shift+F1");
        await _app.Driver.WaitForModalAsync<Core.Dialogs.CommandPaletteDialog>();
        await _app.WaitForTextAsync("Enter:run");
        await _app.TypeAsync(name);
        await _app.WaitForTextAsync(name);
        await _app.PressAsync("Enter");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task Long_ShowsTheAttributesAndDate_WithNoSizeColumn()
    {
        _app = await TerminalApp.StartAsync(seed: (left, _) => File.WriteAllText(Path.Combine(left, Target), "hello"));
        await _app.WaitUntilAsync(s => _app.LeftPanelLists(Target, s), "the seeded file to be listed");

        await RunPaletteCommandAsync("Long listing");

        // rwxrwxrwx on Unix; the RHSA letters, dashes where clear, on Windows.
        var attributes = OperatingSystem.IsWindows()
            ? @"[│\s][RHSA-]{4}\s"
            : @"[r-][w-][x-][r-][w-][x-][r-][w-][x-]";
        var screen = await _app.WaitUntilAsync(
            s => _app.LeftPanel(s).Any(row => row.Contains(Target) && Regex.IsMatch(row, attributes)),
            "the Long row with an attributes column");
        var row = _app.LeftPanel(screen).First(r => r.Contains(Target));
        Assert.IsTrue(Regex.IsMatch(row, @"\d{4}-\d{2}-\d{2}"), "the Long row keeps the date" + Environment.NewLine + screen.Dump());
        Assert.IsFalse(row.Contains(" 5 B") || Regex.IsMatch(row, @"\b5\s*B\b"), "the Long row has no size column" + Environment.NewLine + screen.Dump());
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task Info_OnTheRightPanel_ShowsTheLeftCursorEntry_AndEscGivesTheListBack()
    {
        _app = await TerminalApp.StartAsync(seed: (left, _) =>
        {
            File.WriteAllText(Path.Combine(left, Target), "hello");
        });
        await _app.WaitUntilAsync(s => _app.LeftPanelLists(Target, s), "the seeded file to be listed");

        // Move the left cursor from the '..' row onto the seeded file (folders sort first, so count the rows), then focus the right panel.
        var rows = _app.LeftPanel(_app.Screen).ToList();
        var steps = rows.FindIndex(row => row.Contains(Target)) - rows.FindIndex(row => row.Contains(".."));
        Assert.IsTrue(steps > 0, "the seeded file is listed below '..'");
        for (var i = 0; i < steps; i++)
        {
            await _app.PressAsync("Down");
        }

        await _app.PressAsync("Tab");

        await RunPaletteCommandAsync("Info listing");

        var screen = await _app.WaitUntilAsync(s => _app.RightPanel(s).Any(row => row.Contains(Target)), "the Info rows to name the left cursor entry");
        Assert.IsTrue(_app.RightPanel(screen).Any(row => row.Contains("Type")), screen.Dump());

        await _app.PressAsync("Esc");
        var back = await _app.WaitUntilAsync(s => !_app.RightPanel(s).Any(row => row.Contains("Type")), "the right panel's list to come back");
        Assert.IsFalse(_app.RightPanel(back).Any(row => row.Contains(Target)), back.Dump());
        await _app.WaitForNoStrayFramesAsync();
    }
}
