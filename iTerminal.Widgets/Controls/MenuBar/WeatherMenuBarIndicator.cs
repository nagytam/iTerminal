using iTerminal.Widgets.Services;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Extensions;
using iTerminal.Core.Services;
using iTerminal.Widgets.Services.Weather;

namespace iTerminal.Widgets.Controls.MenuBar;

/// <summary>The weather indicator in the menu bar, shown while the weather indicator setting is on.</summary>
public sealed class WeatherMenuBarIndicator : IMenuBarIndicator
{
    private const int Width = 20;
    private readonly ISettingsService _settingsService;

    public WeatherMenuBarIndicator(WeatherService weatherService, ISettingsService settingsService, IThemeService themeService)
    {
        _settingsService = settingsService;
        Parts = [new MenuBarIndicatorPart(new WeatherIndicator(weatherService, themeService), Width)];
    }

    public int Order => 20;

    public bool IsEnabled => _settingsService.GetAsBool(Settings.IsWeatherIndicatorEnabled, true) ?? true;

    public IReadOnlyList<MenuBarIndicatorPart> Parts { get; }
}
