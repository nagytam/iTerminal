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

    [When("I move the cursor to {string} in the left panel")]
    public async Task WhenIMoveTheCursorToInTheLeftPanel(string name) => await App.SelectEntryAsync(name);

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
