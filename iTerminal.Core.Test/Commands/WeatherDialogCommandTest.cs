using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Weather;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Weather;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class WeatherDialogCommandTest
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

    private sealed class NoProvider : IForecastProvider
    {
        public Task<iTerminal.Core.Services.Weather.Forecast> FetchAsync(SavedPlace place, CancellationToken cancellationToken = default) => throw new InvalidOperationException("not called");

        public Task<IReadOnlyList<PlaceHit>> SearchAsync(string query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PlaceHit>>([]);
    }

    /// <summary>The menu entry has no hot key: every Ctrl+Shift+F-key is taken.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public void ItOpensFromTheToolsMenuWithNoHotKey()
    {
        var dialog = new WeatherDialog(
            new ForecastService(new MemorySettings(), new NoProvider()), Mock.Of<IBeepService>(), new DateTimeProviderService(),
            iTerminal.Core.Test.Helper.UiTestHelper.TestTheme());

        var command = new WeatherDialogCommand(dialog);

        Assert.AreEqual("Weather", command.Title);
        Assert.IsNull(command.Key);
    }
}
