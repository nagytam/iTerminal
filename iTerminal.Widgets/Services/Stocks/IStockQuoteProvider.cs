namespace iTerminal.Widgets.Services.Stocks;

/// <summary>Where stock quotes come from and where symbols are looked up. Async by contract: every call is a network round trip.</summary>
public interface IStockQuoteProvider
{
    /// <summary>
    /// Quotes for <paramref name="symbols"/>, keyed by the symbol as asked. A symbol the source does
    /// not know, or cannot reach right now, is left out rather than failing the others.
    /// </summary>
    Task<IReadOnlyDictionary<string, StockQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default);

    /// <summary>Finds listed stocks by symbol or company name, across every exchange the source covers.</summary>
    Task<IReadOnlyList<StockSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>The daily closing prices over <paramref name="range"/>, oldest first. Empty when the source has none or cannot be reached.</summary>
    Task<IReadOnlyList<StockBar>> GetHistoryAsync(string symbol, StockHistoryRange range, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StockBar>>([]);
}
