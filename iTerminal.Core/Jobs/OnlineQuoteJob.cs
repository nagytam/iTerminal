using iConsole.Jobs;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Services.RuntimeDependencies;
using Microsoft.Playwright;
using System.Globalization;

namespace iTerminal.Core.Jobs;

public class OnlineQuoteJob(
    OnlineQuoteService onlineQuoteService,
    ICacheService cacheService,
    IRuntimeDependencyService runtimeDependencies) : Job
{
    internal const string BTCUrl = @"https://www.swissquote.ch/trading-platform/#fullQuote/EX0X00XBT000/31337_USD";
    internal const string ETHUrl = @"https://www.swissquote.ch/trading-platform/#fullQuote/EX0X00ETH000/31337_USD";

    private readonly OnlineQuoteService _onlineQuoteService = onlineQuoteService;
    private readonly ICacheService _cacheService = cacheService;
    private readonly IRuntimeDependencyService _runtimeDependencies = runtimeDependencies;

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        // Load from cache if available
        var cachedInstruments = await _cacheService.GetCachedInstrumentsAsync();
        if (cachedInstruments.Count > 0)
        {
            InstrumentQuoteHelper.PrimeFromCache(_onlineQuoteService, cachedInstruments, nameof(OnlineQuoteJob));
            _onlineQuoteService.IsFromCache = true;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            // Playwright is installed by the setup dialog, not by this job: until it is, there is nothing to drive.
            if (!_runtimeDependencies.IsInstalled(PlaywrightRuntimeDependency.DependencyId))
            {
                await Task.Delay(15000, cancellationToken);
                continue;
            }

            try
            {
                _onlineQuoteService.IsRefreshing = true;
                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync();

                var tasks = new List<Task>();

                #region BTC
                tasks.Add(Task.Run(async () =>
                {
                    var page = await browser.NewPageAsync();
                    await page.GotoAsync(BTCUrl);
                    await ParseQuotePageAsync(page);
                    await page.CloseAsync();
                }, cancellationToken));
                #endregion

                #region ETH
                tasks.Add(Task.Run(async () =>
                {
                    var page = await browser.NewPageAsync();
                    await page.GotoAsync(ETHUrl);
                    await ParseQuotePageAsync(page);
                    await page.CloseAsync();
                }, cancellationToken));
                #endregion

                await Task.WhenAll(tasks);
                _onlineQuoteService.IsFromCache = false;
                _onlineQuoteService.LastRefreshFailed = false;
            }
            catch (PlaywrightException)
            {
                // The browser did not start: its install is no longer complete, so the dialog offers it again.
                _onlineQuoteService.LastRefreshFailed = true;
                _runtimeDependencies.InvalidateInstalled(PlaywrightRuntimeDependency.DependencyId);
            }
            catch
            {
                _onlineQuoteService.LastRefreshFailed = true;
            }
            finally
            {
                _onlineQuoteService.IsRefreshing = false;
            }
            await Task.Delay(15000, cancellationToken);
        }
    }

    // Class selectors rather than exact-@class XPath: the page adds modifier classes freely, and a
    // redesign renamed the price element (FullQuote_BigPriceMainValue → FullQuote_PriceWithChange__price),
    // which left the quote indicator silently empty. Both spellings are accepted.
    internal const string NameSelector = ".FullQuote_ProductName";
    internal const string PriceSelector = ".FullQuote_PriceWithChange__price, .FullQuote_BigPriceMainValue";
    internal const string CurrencySelector = ".FullQuote_PrimaryInfo";
    internal const string TimestampSelector = ".FullQuote_Timestamp";

    internal async Task ParseQuotePageAsync(IPage page)
    {
        var name = (await page.Locator(NameSelector).First.InnerTextAsync()).Trim();
        var price = (await page.Locator(PriceSelector).First.InnerTextAsync()).Trim();
        var currency = (await page.Locator(CurrencySelector).First.InnerTextAsync()).Trim();
        var timestamp = (await page.Locator(TimestampSelector).First.InnerTextAsync()).Trim();

        await InstrumentQuoteHelper.RecordQuoteAsync(
            _onlineQuoteService,
            _cacheService,
            source: nameof(OnlineQuoteJob),
            name: name,
            currency: currency,
            instrumentType: InstrumentType.CryptoCurrency,
            value: decimal.Parse(price, new NumberFormatInfo() { NumberDecimalSeparator = ".", NumberGroupSeparator = "'" }),
            valueDateTime: DateTime.Parse(timestamp, CultureInfo.InvariantCulture),
            sourceDateTime: DateTime.UtcNow);
    }
}
