using iConsole.Jobs;
using iTerminal.Core.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Portfolio;

namespace iTerminal.Core.Jobs;

public class PortfolioQuoteJob(
    IPortfolioService portfolioService,
    OnlineQuoteService onlineQuoteService,
    IQuoteProvider quoteProvider,
    ICacheService cacheService) : Job
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(60);

    private readonly IPortfolioService _portfolioService = portfolioService;
    private readonly OnlineQuoteService _onlineQuoteService = onlineQuoteService;
    private readonly IQuoteProvider _quoteProvider = quoteProvider;
    private readonly ICacheService _cacheService = cacheService;

    internal static string BuildStooqSymbol(PortfolioHolding holding) =>
        string.IsNullOrWhiteSpace(holding.Exchange) ? holding.Symbol : $"{holding.Symbol}.{holding.Exchange}";

    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cachedInstruments = await _cacheService.GetCachedInstrumentsAsync();
            InstrumentQuoteHelper.PrimeFromCache(
                _onlineQuoteService,
                cachedInstruments.Where(c => c.InstrumentType == (int)InstrumentType.Stock),
                nameof(PortfolioQuoteJob));
        }
        catch
        {
            // Cache priming is best-effort; fall through to the live refresh loop regardless.
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(cancellationToken);
            }
            catch
            {
                // Transient network/service failures are tolerated; retried next tick.
            }

            try
            {
                await Task.Delay(RefreshInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var holdings = await _portfolioService.GetHoldingsAsync();
        if (holdings.Count == 0)
        {
            return;
        }

        var holdingsBySymbol = holdings
            .GroupBy(BuildStooqSymbol)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var quotes = await _quoteProvider.GetQuotesAsync(holdingsBySymbol.Keys.ToList(), cancellationToken);
        foreach (var (symbol, quote) in quotes)
        {
            if (!holdingsBySymbol.TryGetValue(symbol, out var holding))
            {
                continue;
            }

            await InstrumentQuoteHelper.RecordQuoteAsync(
                _onlineQuoteService,
                _cacheService,
                source: nameof(PortfolioQuoteJob),
                name: symbol,
                currency: holding.Currency,
                instrumentType: InstrumentType.Stock,
                value: quote.Close,
                valueDateTime: quote.Timestamp,
                sourceDateTime: quote.Timestamp);
        }
    }
}
