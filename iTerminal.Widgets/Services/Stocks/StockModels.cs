namespace iTerminal.Widgets.Services.Stocks;

/// <summary>A stock on the watchlist: the symbol the quote source knows it by, and a name to show before a quote has landed.</summary>
public sealed record WatchedStock(string Symbol, string Name);

/// <summary>One quote as the source reported it. Prices are in <see cref="Currency"/>.</summary>
public sealed record StockQuote(
    string Symbol,
    string Name,
    decimal Price,
    decimal PreviousClose,
    string Currency,
    string Exchange,
    DateTime? MarketTime,
    decimal? DayHigh = null,
    decimal? DayLow = null,
    decimal? YearHigh = null,
    decimal? YearLow = null,
    long? Volume = null)
{
    public decimal Change => Price - PreviousClose;

    /// <summary>Change since the previous close in percent; 0 when the source gave no previous close.</summary>
    public decimal ChangePercent => PreviousClose == 0 ? 0 : Change / PreviousClose * 100m;
}

/// <summary>A match for a search: the symbol to watch and the exchange it trades on, since one company is listed on several.</summary>
public sealed record StockSearchHit(string Symbol, string Name, string Exchange, string Type);

/// <summary>One day's closing price as the source reported it, in the security's currency.</summary>
public sealed record StockBar(DateTime Date, decimal Close);

/// <summary>How far back a price history goes. Every range is daily closes.</summary>
public enum StockHistoryRange
{
    OneMonth,
    ThreeMonths,
    OneYear,
    FiveYears,
}
