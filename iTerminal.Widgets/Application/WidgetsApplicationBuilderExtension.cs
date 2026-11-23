using iTerminal.Widgets.Services;
using System.Reflection;
using iConsole.Commands;
using iTerminal.Core.Application;
using iTerminal.Core.Extensions;
using iConsole.Jobs;
using iTerminal.Core.Services;
using iTerminal.Core.Services.RuntimeDependencies;
using iTerminal.Widgets.Jobs;
using iTerminal.Widgets.Services.RuntimeDependencies;
using iTerminal.Widgets.Services;
using iTerminal.Widgets.Commands;
using iTerminal.Widgets.Controls.MenuBar;
using iTerminal.Widgets.Dialogs.Stocks;
using iTerminal.Widgets.Dialogs.Terminal;
using iTerminal.Widgets.Dialogs.Weather;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace iTerminal.Widgets.Application;

/// <summary>
/// Registers the online information widgets: their services, dialogs, jobs and runtime dependency, and the entries they
/// add to the Tools menu, the hot-key row and the menu bar. The composition root calls this after <c>AddiTerminal</c>;
/// iTerminal.Core cannot call it, since it does not reference this project.
/// </summary>
public static class WidgetsApplicationBuilderExtension
{
    public static IServiceCollection AddWidgets(this IServiceCollection services, IConfiguration? configuration = null)
    {
        string databaseDirectory = iTerminalApplicationBuilderExtension.ResolveDataDirectory(configuration);

        if (configuration is not null)
        {
            services.Configure<iTerminal.Widgets.Services.Translation.TranslationOptions>(configuration.GetSection("Translation"));
        }

        services.AddOptions<iTerminal.Widgets.Services.Translation.TranslationOptions>();

        services.AddSingleton<iTerminal.Widgets.Model.Radio.RadioStationStore>();
        services.AddTransient<iTerminal.Widgets.Dialogs.Radio.OnlineRadioDialog>();
        // Alt+F10: the ASCII-art OpenStreetMap. Tiles are saved (and .mbtiles files read) under <data directory>/maps,
        // so it works offline; a test replaces the tile service so no run reaches the network.
        services.AddSingleton<iTerminal.Widgets.Services.Map.MapStore>();
        services.AddSingleton<iTerminal.Widgets.Services.Map.IMapSearchService, iTerminal.Widgets.Services.Map.MapSearchService>();
        services.AddSingleton<iTerminal.Widgets.Services.Map.IMapTileService>(provider => new iTerminal.Widgets.Services.Map.MapTileService(
            provider.GetRequiredService<iTerminal.Core.Services.Web.IWebFetcher>(),
            provider.GetRequiredService<iTerminal.Widgets.Services.Map.MapStore>(),
            Path.Combine(databaseDirectory, "maps")));
        services.AddTransient<iTerminal.Widgets.Dialogs.Map.MapDialog>();
        // Alt+F7: the full-screen stock browser. Quotes come from Yahoo Finance; a test replaces the
        // provider so no run reaches the network.
        services.AddSingleton<iTerminal.Widgets.Services.Stocks.IStockQuoteProvider, iTerminal.Widgets.Services.Stocks.YahooStockQuoteProvider>();
        services.AddSingleton<iTerminal.Widgets.Services.Stocks.IStockService, iTerminal.Widgets.Services.Stocks.StockService>();
        services.AddTransient<iTerminal.Widgets.Dialogs.Stocks.StockBrowserDialog>();
        services.AddTransient<iTerminal.Widgets.Dialogs.Terminal.MarketTerminalDialog>();
        // Ctrl+Shift+F4: the translator. A LibreTranslate-compatible API (Translation:ApiUrl) and the connected AI
        // model are two providers behind one service; a test replaces the providers so no run reaches the network.
        services.AddSingleton<iTerminal.Widgets.Services.Translation.ILanguageDetector, iTerminal.Widgets.Services.Translation.LanguageDetector>();
        services.AddSingleton<iTerminal.Widgets.Services.Translation.ITranslationProvider>(provider =>
        {
            var translation = provider.GetRequiredService<IOptions<iTerminal.Widgets.Services.Translation.TranslationOptions>>().Value;
            return new iTerminal.Widgets.Services.Translation.LibreTranslateProvider(translation.ApiUrl, () =>
            {
                string? key = Environment.GetEnvironmentVariable(translation.ApiKeyEnvVar);
                if (!string.IsNullOrEmpty(key))
                {
                    return key;
                }

                try
                {
                    return provider.GetRequiredService<IPasswordManager>().GetSecret(iTerminal.Widgets.Services.Translation.TranslationOptions.ApiKeyPasswordId);
                }
                catch (Exception exception) when (exception is InvalidOperationException or IOException)
                {
                    return null;
                }
            });
        });
        services.AddSingleton<iTerminal.Widgets.Services.Translation.ITranslationProvider, iTerminal.Widgets.Services.Translation.LlmTranslationProvider>();
        services.AddSingleton<iTerminal.Widgets.Services.Translation.ITranslationService, iTerminal.Widgets.Services.Translation.TranslationService>();
        services.AddTransient<iTerminal.Widgets.Dialogs.Translator.TranslatorDialog>();
        // Ctrl+Shift+F5: the currency converter. Rates are the European Central Bank's daily file; a test replaces
        // the provider so no run reaches the network.
        services.AddSingleton<iTerminal.Widgets.Services.Currency.ICurrencyRateProvider, iTerminal.Widgets.Services.Currency.EcbCurrencyRateProvider>();
        services.AddSingleton<iTerminal.Widgets.Services.Currency.ICurrencyService, iTerminal.Widgets.Services.Currency.CurrencyService>();
        services.AddTransient<iTerminal.Widgets.Dialogs.CurrencyConverter.CurrencyConverterDialog>();
        // Tools > Weather: the forecast and place names are Open-Meteo's (no key). Places, the home place and the last forecast
        // of each are kept in the settings; a test replaces the provider so no run reaches the network.
        services.AddSingleton<iTerminal.Widgets.Services.Weather.IForecastProvider, iTerminal.Widgets.Services.Weather.OpenMeteoForecastProvider>();
        services.AddSingleton<iTerminal.Widgets.Services.Weather.IForecastService, iTerminal.Widgets.Services.Weather.ForecastService>();
        services.AddTransient<iTerminal.Widgets.Dialogs.Weather.WeatherDialog>();
        // Ctrl+Shift+W: the encyclopedia. Wikipedia and Wiktionary are read through the MediaWiki API; a test replaces the
        // provider so no run reaches the network.
        services.AddSingleton<iTerminal.Widgets.Services.Encyclopedia.IEncyclopediaProvider, iTerminal.Widgets.Services.Encyclopedia.WikiEncyclopediaProvider>();
        services.AddSingleton<iTerminal.Widgets.Services.Encyclopedia.IEncyclopediaService, iTerminal.Widgets.Services.Encyclopedia.EncyclopediaService>();
        services.AddTransient<iTerminal.Widgets.Dialogs.Encyclopedia.EncyclopediaDialog>();
        services.AddSingleton<iTerminal.Widgets.Services.YouTube.IYouTubeService, iTerminal.Widgets.Services.YouTube.YtDlpYouTubeService>();
        services.AddTransient<iTerminal.Widgets.Dialogs.YouTube.YouTubeDialog>();
        services.AddSingleton<IJob, OnlineQuoteJob>();
        services.AddSingleton<IJob, WeatherJob>();
        services.AddSingleton<OnlineQuoteService>();
        services.AddSingleton<IRuntimeDependency, YtDlpRuntimeDependency>();
        services.AddSingleton<WeatherService>();

        // The Tools menu entries and hot keys resolve their dialog when the menu bar is built, one instance each, as before.
        AddToolsMenuEntry<StockBrowserDialog>(services, "office", dialog => new StockBrowserDialogCommand(dialog));
        AddToolsMenuEntry<MarketTerminalDialog>(services, "office", dialog => new MarketTerminalDialogCommand(dialog));
        AddToolsMenuEntry<iTerminal.Widgets.Dialogs.Map.MapDialog>(services, "office", dialog => new MapDialogCommand(dialog));
        AddToolsMenuEntry<iTerminal.Widgets.Dialogs.Translator.TranslatorDialog>(services, "office", dialog => new TranslatorDialogCommand(dialog));
        AddToolsMenuEntry<iTerminal.Widgets.Dialogs.CurrencyConverter.CurrencyConverterDialog>(services, "office", dialog => new CurrencyConverterDialogCommand(dialog));
        AddToolsMenuEntry<WeatherDialog>(services, "office", dialog => new WeatherDialogCommand(dialog));
        AddToolsMenuEntry<iTerminal.Widgets.Dialogs.Encyclopedia.EncyclopediaDialog>(services, "office", dialog => new EncyclopediaDialogCommand(dialog));
        AddToolsMenuEntry<iTerminal.Widgets.Dialogs.YouTube.YouTubeDialog>(services, "media", dialog => new YouTubeDialogCommand(dialog));
        AddToolsMenuEntry<iTerminal.Widgets.Dialogs.Radio.OnlineRadioDialog>(services, "media", dialog => new OnlineRadioDialogCommand(dialog));

        services.AddSingleton<IStatusBarHotKey>(provider => new WidgetHotKey(
            "Alt-F7-Stocks", 15, new StockBrowserDialogCommand(provider.GetRequiredService<StockBrowserDialog>())));
        services.AddSingleton<IStatusBarHotKey>(provider => new WidgetHotKey(
            "Shift-Ctrl-F4-Translate", 18, new TranslatorDialogCommand(provider.GetRequiredService<iTerminal.Widgets.Dialogs.Translator.TranslatorDialog>())));
        services.AddSingleton<IStatusBarHotKey>(provider => new WidgetHotKey(
            "Shift-Ctrl-F5-Currency", 22, new CurrencyConverterDialogCommand(provider.GetRequiredService<iTerminal.Widgets.Dialogs.CurrencyConverter.CurrencyConverterDialog>())));

        services.AddSingleton<IOpenableDialogContribution>(new OpenableWidgetDialog<iTerminal.Widgets.Dialogs.CurrencyConverter.CurrencyConverterDialog>("currency", "currencyconverter", "currency converter"));
        services.AddSingleton<IOpenableDialogContribution>(new OpenableWidgetDialog<iTerminal.Widgets.Dialogs.Encyclopedia.EncyclopediaDialog>("encyclopedia", "wikipedia", "wiki", "britannica", "dictionary"));
        services.AddSingleton<IOpenableDialogContribution>(new OpenableWidgetDialog<iTerminal.Widgets.Dialogs.Radio.OnlineRadioDialog>("radio", "online_radio", "internet_radio", "radio_stations"));
        services.AddSingleton<IOpenableDialogContribution>(new OpenableWidgetDialog<iTerminal.Widgets.Dialogs.Translator.TranslatorDialog>("translator", "translate", "translation"));

        services.AddSingleton<IMenuBarIndicator, QuoteMenuBarIndicator>();
        services.AddSingleton<IMenuBarIndicator, WeatherMenuBarIndicator>();

        // The key map may bind this project's commands by name, so its assembly is scanned with Core's.
        services.AddSingleton<ICommandAssemblyContribution>(new WidgetsCommandAssembly(typeof(WidgetsApplicationBuilderExtension).Assembly));

        return services;
    }

    private static void AddToolsMenuEntry<TDialog>(IServiceCollection services, string group, Func<TDialog, ICommand> command)
        where TDialog : notnull =>
        services.AddSingleton<IToolsMenuContribution>(provider => new WidgetToolsMenuEntry(group, command(provider.GetRequiredService<TDialog>())));
}
