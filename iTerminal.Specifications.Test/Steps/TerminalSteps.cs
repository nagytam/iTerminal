using iTerminal.Test;
using Reqnroll;

namespace iTerminal.Specifications.Test.Steps;

/// <summary>
/// Steps that drive the real application on a virtual terminal. Reqnroll creates one instance per
/// scenario, so the running app is held in a field; <see cref="StopAppAfterScenario"/> shuts it down.
/// </summary>
[Binding]
public sealed class TerminalSteps
{
    private const int MinimumThreads = 64;

    private TerminalApp? _app;

    /// <summary>
    /// Raises the thread pool's floor before any scenario runs, as the end-to-end suite does: every sandboxed
    /// app leaves a few threads behind, and the pool otherwise grows too slowly for a later app's first frame.
    /// </summary>
    [BeforeTestRun]
    public static void RaiseThreadPoolFloor()
    {
        ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
        ThreadPool.SetMinThreads(
            Math.Max(workerThreads, MinimumThreads),
            Math.Max(completionPortThreads, MinimumThreads));
    }

    private TerminalApp App => _app ?? throw new InvalidOperationException("iTerminal has not been started in this scenario.");

    [Given("iTerminal has just started")]
    public async Task GivenITerminalHasJustStarted() => _app = await TerminalApp.StartAsync();

    [AfterScenario]
    public async Task StopAppAfterScenario()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    [When("I press {string}")]
    public async Task WhenIPress(string key) => await App.PressAsync(key);

    [When("I type {string}")]
    public async Task WhenIType(string text) => await App.TypeAsync(text);

    [Given("a file {string} appears in the left folder")]
    public void GivenAFileAppearsInTheLeftFolder(string name) =>
        File.WriteAllText(Path.Combine(App.LeftRoot, name), "new");

    [When("I move the cursor to {string} in the left panel")]
    public async Task WhenIMoveTheCursorToInTheLeftPanel(string name) => await App.SelectEntryAsync(name);

    [When("I move the cursor to {string} in the right panel")]
    public async Task WhenIMoveTheCursorToInTheRightPanel(string name)
    {
        Assert.AreEqual(Core.Services.ContentModelService.Right, App.ContentModels.FocusedPanel, "the right panel has the keyboard");
        await App.SelectEntryAsync(name);
    }

    [Then("the right panel has the keyboard")]
    public void ThenTheRightPanelHasTheKeyboard() =>
        Assert.AreEqual(Core.Services.ContentModelService.Right, App.ContentModels.FocusedPanel);

    [Then("the screen shows {string}")]
    public async Task ThenTheScreenShows(string text) =>
        await App.WaitUntilAsync(screen => screen.Contains(text), $"the screen to show '{text}'");

    [Then("the screen does not show {string}")]
    public async Task ThenTheScreenDoesNotShow(string text) =>
        await App.WaitUntilAsync(screen => !screen.Contains(text), $"the screen to stop showing '{text}'");

    [Then("each panel is titled with its folder")]
    public void ThenEachPanelIsTitledWithItsFolder()
    {
        var top = App.Screen.Line(1);

        StringAssert.Contains(top, Path.GetFileName(App.LeftRoot));
        StringAssert.Contains(top, Path.GetFileName(App.RightRoot));
        Assert.IsTrue(top.IndexOf("left", StringComparison.Ordinal) < top.IndexOf("right", StringComparison.Ordinal),
            "the left panel is on the left");
    }

    [Then("{string} is listed before {string} in the left panel")]
    public void ThenIsListedBeforeInTheLeftPanel(string first, string second)
    {
        var names = App.LeftPanel()
            .Select(row => row.Length > 1 ? row[1..].Split(' ')[0] : string.Empty)
            .ToList();

        Assert.IsTrue(names.IndexOf(first) >= 0 && names.IndexOf(first) < names.IndexOf(second),
            string.Join(Environment.NewLine, names));
    }

    [Then("the panel footers show {int} and {int} entries")]
    public async Task ThenThePanelFootersShowEntries(int left, int right) =>
        await App.WaitUntilAsync(
            screen => screen.Contains($" {left} entries ") && screen.Contains($" {right} entries "),
            $"the footers to count {left} and {right} entries");

    [Then("the splash screen is gone")]
    public async Task ThenTheSplashScreenIsGone()
    {
        Assert.IsFalse(App.Screen.Contains("Welcome to iTerminal"));
        await App.WaitForNoStrayFramesAsync();
    }

    [Then("nothing beeped")]
    public void ThenNothingBeeped() =>
        Assert.AreEqual(0, App.Driver.Console.BeepCount, "nothing beeped while starting");

    [Then("the database is in the data folder")]
    public void ThenTheDatabaseIsInTheDataFolder() =>
        Assert.IsTrue(File.Exists(Path.Combine(App.DataDirectory, "iTerminal.sqlite")),
            "the database belongs in the folder the configuration names, not in %APPDATA%");

    [Then("the menu bar is on the top row")]
    public void ThenTheMenuBarIsOnTheTopRow() =>
        StringAssert.StartsWith(App.Screen.Line(0), "File Edit Navigate Tools Help");

    [Then("the key bar starts with {string}")]
    public void ThenTheKeyBarStartsWith(string text) =>
        StringAssert.StartsWith(KeyBar(), text);

    [Then("the key bar contains {string}")]
    public void ThenTheKeyBarContains(string text) =>
        StringAssert.Contains(KeyBar(), text);

    [Then("the left panel lists {string}")]
    public async Task ThenTheLeftPanelLists(string name) =>
        await App.WaitUntilAsync(screen => App.LeftPanelLists(name, screen), $"the left panel to list '{name}'");

    [Then("the right panel lists {string}")]
    public async Task ThenTheRightPanelLists(string name) =>
        await App.WaitUntilAsync(screen => App.RightPanelLists(name, screen), $"the right panel to list '{name}'");

    [Then("the left panel has the keyboard")]
    public void ThenTheLeftPanelHasTheKeyboard() =>
        Assert.AreEqual(Core.Services.ContentModelService.Left, App.ContentModels.FocusedPanel);

    [Then("the cursor is on {string}")]
    public async Task ThenTheCursorIsOn(string name)
    {
        await App.WaitUntilAsync(
            () => CursorName() == name,
            $"the cursor to be on '{name}'");
        Assert.AreEqual(name, CursorName());
    }

    // The parent row ("..") has no Name, so the cursor is read by its display name, as the end-to-end tests do.
    private string? CursorName() => App.FocusedModel?.GetFocusedEntry()?.DisplayName.Trim();

    private string KeyBar() => App.Screen.Line(App.Screen.Height - 1);
}
