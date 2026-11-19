using iConsole.Services;
using iTerminal.Core.Dialogs.Weather;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Weather;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Weather;

/// <summary>Tools > Weather: the views are drawn from the kept forecast, and a letter typed in the search box goes to the box, not to the list.</summary>
[TestClass]
public class WeatherDialogTest
{
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = [];
        public bool IsDirty => false;
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
        public void Set(string key, string value) => _values[key] = value;
        public void Write() { }
    }

    private sealed class CountingProvider : IForecastProvider
    {
        public int FetchCount { get; private set; }

        public Task<Forecast> FetchAsync(SavedPlace place, CancellationToken cancellationToken = default)
        {
            FetchCount++;
            return Task.FromResult(Sample());
        }

        public Task<IReadOnlyList<PlaceHit>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlaceHit>>([]);
    }

    private static readonly SavedPlace Budapest = new("Budapest", "Budapest", "Hungary", 47.4979, 19.0402, "Europe/Budapest");

    /// <summary>Partly cloudy now; rain at 15:00 over the next 24 hours; rain on the second of seven days.</summary>
    private static Forecast Sample()
    {
        var start = new DateTime(2026, 11, 18, 10, 0, 0);
        var hours = Enumerable.Range(0, 24).Select(hour => new HourlyPoint(
            start.AddHours(hour), 8 + hour % 6, hour == 5 ? 1.5 : 0, 10, 3)).ToList();
        var days = Enumerable.Range(0, 7).Select(day => new DailyPoint(
            new DateOnly(2026, 11, 18).AddDays(day), 2, 10, day == 1 ? 4.5 : 0, day == 1 ? 80 : 5, day == 1 ? 63 : 2)).ToList();
        return new Forecast(new CurrentConditions(start, 9.5, 2, true), hours, days, new DateTimeOffset(start, TimeSpan.Zero));
    }

    private static (WeatherDialog Dialog, ForecastService Service, CountingProvider Provider) Create()
    {
        var provider = new CountingProvider();
        var service = new ForecastService(new MemorySettings(), provider);
        service.AddPlace(Budapest);
        service.SetHome(Budapest);
        var dialog = new WeatherDialog(service, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        return (dialog, service, provider);
    }

    [TestMethod]
    public void TheHourlyViewIsDrawnAsALineAndARainRow()
    {
        var lines = WeatherDialog.HourlyLines(Sample().Hourly, width: 40, height: 6);

        Assert.AreEqual(6, lines.Count, "the chart, the rain row and the hour row fill the height");
        Assert.IsTrue(lines.All(line => line.Length <= 40), "no line is wider than the pane");
        Assert.IsTrue(lines[^2].StartsWith("Rain", StringComparison.Ordinal));
        StringAssert.Contains(lines[^1], "06", "the hour labels are every third hour");
    }

    [TestMethod]
    public void AWideDayIsSampledToTheColumnsItHas()
    {
        var lines = WeatherDialog.HourlyLines(Sample().Hourly, width: 16, height: 6);

        Assert.IsTrue(lines.All(line => line.Length <= 16), "24 hours in 10 columns");
    }

    [TestMethod]
    public void TheDailyViewHasAHeadingAndOneRowPerDay()
    {
        var lines = WeatherDialog.DailyLines(Sample().Daily, width: 60, height: 9);

        Assert.AreEqual(8, lines.Count, "the heading and seven days");
        StringAssert.Contains(lines[0], "Chance");
        StringAssert.Contains(lines[2], "80%", "the second day's chance of rain");
    }

    [TestMethod]
    public void ANarrowDailyTableDropsColumnsRatherThanRunningPastTheEdge()
    {
        var lines = WeatherDialog.DailyLines(Sample().Daily, width: 30, height: 9);

        Assert.IsTrue(lines.All(line => line.Length <= 30), "every row fits the pane");
        StringAssert.Contains(lines[0], "Min", "the extremes stay");
        Assert.IsFalse(lines[0].Contains("Chance", StringComparison.Ordinal), "the chance is the first column to go");
    }

    [TestMethod]
    public void TheListGivesTheLeftPaneAtMostHalfTheDialog()
    {
        Assert.IsTrue(WeatherDialog.LeftWidth(40) <= 20);
        Assert.AreEqual(26, WeatherDialog.LeftWidth(80));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ItShowsTheHomePlaceAndItsConditions()
    {
        var (dialog, service, _) = Create();
        await service.RefreshAsync(Budapest);
        await ShowAsync(dialog);

        var screen = Render(dialog);

        AssertScreenContains(screen, "Budapest");
        AssertScreenContains(screen, "Partly cloudy");
        AssertScreenContains(screen, "Next 24 hours");
        AssertScreenContains(screen, "Rain");
        dialog.Close();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VSwitchesToTheDailyTable()
    {
        var (dialog, service, _) = Create();
        await service.RefreshAsync(Budapest);
        await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Char('v'));

        AssertScreenContains(Render(dialog), "Next 7 days");
        AssertScreenContains(Render(dialog), "Chance");
        dialog.Close();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALetterTypedInTheSearchBoxDoesNotFireAShortcut()
    {
        var (dialog, service, _) = Create();
        await ShowAsync(dialog);
        await dialog.HandleKeyEventAsync(Char('a'));

        await dialog.HandleKeyEventAsync(Char('d'));

        Assert.AreEqual(1, service.Places.Count, "d in the query box is typed, it does not remove the place");
        dialog.Close();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RenderingFetchesNothing()
    {
        var (dialog, _, provider) = Create();
        await ShowAsync(dialog);

        Render(dialog);
        Render(dialog);

        Assert.AreEqual(0, provider.FetchCount, "Draw reads the kept forecast; the fetch runs from OnIdle, never from Draw");
        dialog.Close();
    }
}
