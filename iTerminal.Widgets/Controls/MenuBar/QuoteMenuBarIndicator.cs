using iTerminal.Widgets.Services;
using iConsole.Services;
using iTerminal.Core.Data;
using iTerminal.Core.Extensions;
using iTerminal.Core.Services;

namespace iTerminal.Widgets.Controls.MenuBar;

/// <summary>The Ethereum and Bitcoin quote indicators, shown together while the quotes indicator setting is on.</summary>
public sealed class QuoteMenuBarIndicator : IMenuBarIndicator
{
    private readonly ISettingsService _settingsService;

    public QuoteMenuBarIndicator(OnlineQuoteService onlineQuoteService, ISettingsService settingsService, IThemeService themeService)
    {
        _settingsService = settingsService;
        Parts =
        [
            new MenuBarIndicatorPart(new QuoteIndicator("Ethereum", onlineQuoteService, themeService), 17),
            new MenuBarIndicatorPart(new QuoteIndicator("Bitcoin", onlineQuoteService, themeService), 18),
        ];
    }

    public int Order => 10;

    public bool IsEnabled => _settingsService.GetAsBool(Settings.IsQuotesIndicatorEnabled, true) ?? true;

    public IReadOnlyList<MenuBarIndicatorPart> Parts { get; }
}
