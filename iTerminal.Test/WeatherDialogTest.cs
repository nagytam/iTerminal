using iConsole.Test.Core.Automation;

namespace iTerminal.Test;

/// <summary>Tools > Weather, used the way a person uses it: find a place, save it, read its forecast, switch views, close.</summary>
[TestClass]
public class WeatherDialogTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await (_app?.DisposeAsync() ?? ValueTask.CompletedTask);

    [Timeout(120_000)]
    [TestMethod]
    public async Task ToolsWeatherFindsASavedPlaceAndShowsItsForecast()
    {
        await _app.Driver.ClickAsync("Tools");
        await _app.WaitUntilAsync(
            () => _app.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar,
            "the Tools menu to take the keyboard");
        await _app.OpenSubmenuAsync("Office");
        // The entry is in the Office submenu, below the menu bar row; the bar's own weather indicator is on row 0 and does not count.
        // Opening the submenu draws its entries already, so wait for Weather to be the selected row ("<Weather ...>") before Enter.
        for (int attempt = 0; attempt < 100 && !_app.Screen.Lines.Skip(1).Any(line => line.Contains("<Weather", StringComparison.Ordinal)); attempt++)
        {
            ConsoleSnapshot previous = _app.Screen;
            await _app.PressAsync("Down");
            await _app.Driver.WaitUntilAsync(screen => !screen.HasSameContent(previous), "the Tools menu to redraw after moving selection", TimeSpan.FromSeconds(2));
        }

        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.Weather.WeatherDialog, "the weather dialog to open from Tools");

        // No place is saved yet, so the dialog opens on the search.
        await _app.WaitForTextAsync("Find a place by name");
        await _app.TypeAsync("Budapest");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Budapest, Budapest, Hungary");

        await _app.PressAsync("Enter");
        // "Saved Budapest" is the status line only until the forecast lands and the dialog says "Forecast updated" in its place; the sandbox
        // answers at once, so on a slow runner the poll can miss the first. Either line shows the place was saved.
        await _app.WaitUntilAsync(
            screen => screen.Contains("Saved Budapest") || screen.Contains("Forecast updated"),
            "the save to be reported on the status line");

        // The place is saved, so its forecast is fetched: the conditions and the next 24 hours.
        await _app.WaitForTextAsync("9.5°C");
        await _app.WaitForTextAsync("Partly cloudy");
        await _app.WaitForTextAsync("Next 24 hours");

        await _app.PressAsync("v");
        await _app.WaitForTextAsync("Next 7 days");
        await _app.WaitForTextAsync("Chance");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }
}
