using System.Text.Json;
using Serilog;

namespace iTerminal.Core.Services.Stocks;

/// <summary>
/// The stock watchlist and its live quotes, as the stock browser sees them. Quotes are
/// fetched in the background while at least one browser is open (<see cref="Watch"/>), and
/// the browser is told through <see cref="Changed"/> — it never waits on the network itself, so a
/// listing is always answered from what is already here.
/// </summary>
public interface IStockService
{
    /// <summary>Raised from a background thread when the watchlist or a quote changed.</summary>
    event EventHandler? Changed;

    /// <summary>When the last quotes landed, or null before the first.</summary>
    DateTime? LastUpdated { get; }

    /// <summary>Why the last refresh got no quotes, or empty.</summary>
    string LastError { get; }

    /// <summary>The stocks being watched, in the order the user keeps them.</summary>
    IReadOnlyList<WatchedStock> Watchlist { get; }

    StockQuote? GetQuote(string symbol);

    /// <summary>
    /// Keeps the quotes fresh until the returned handle is disposed. The first call starts the
    /// refresh loop and the last one stops it, so nothing is fetched while no panel shows the list.
    /// </summary>
    IDisposable Watch();

    /// <summary>Fetches the quotes now (the loop does it on a timer; this is for a manual refresh).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a stock on the list after checking that the source can quote it. Throws
    /// <see cref="InvalidOperationException"/> in words the user can act on — already listed, or no quote.
    /// </summary>
    Task<WatchedStock> AddAsync(string symbol, string? name = null, CancellationToken cancellationToken = default);

    /// <summary>Takes <paramref name="symbol"/> off the list; false when it was not on it.</summary>
    bool Remove(string symbol);
}

public sealed class StockService : IStockService, IDisposable
{
    /// <summary>The settings key holding the watchlist as JSON. Absent means "never touched": the defaults apply.</summary>
    public const string WatchlistKey = "Stocks.Watchlist";

    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromSeconds(15);

    /// <summary>What a fresh install watches. SpaceX trades on Nasdaq as SPCX.</summary>
    public static readonly IReadOnlyList<WatchedStock> Defaults =
    [
        new("AAPL", "Apple"),
        new("MSFT", "Microsoft"),
        new("TSLA", "Tesla"),
        new("GOOGL", "Google (Alphabet)"),
        new("NVDA", "Nvidia"),
        new("INTC", "Intel"),
        new("SPCX", "SpaceX"),
    ];

    private readonly ISettingsService _settingsService;
    private readonly IStockQuoteProvider _provider;
    private readonly ILogger? _logger;
    private readonly TimeSpan _refreshInterval;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, StockQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);

    private List<WatchedStock>? _watchlist;
    private int _watchers;
    private CancellationTokenSource? _loopCancellation;
    private Task _loop = Task.CompletedTask;
    private DateTime? _lastUpdated;
    private string _lastError = string.Empty;

    public StockService(ISettingsService settingsService, IStockQuoteProvider provider, ILogger? logger = null, TimeSpan? refreshInterval = null)
    {
        _settingsService = settingsService;
        _provider = provider;
        _logger = logger;
        _refreshInterval = refreshInterval ?? DefaultRefreshInterval;
    }

    public event EventHandler? Changed;

    public DateTime? LastUpdated
    {
        get
        {
            lock (_lock)
            {
                return _lastUpdated;
            }
        }
    }

    public string LastError
    {
        get
        {
            lock (_lock)
            {
                return _lastError;
            }
        }
    }

    /// <summary>The refresh loop started last. Nothing waits on it; it exists so a test can.</summary>
    internal Task LoopTask
    {
        get
        {
            lock (_lock)
            {
                return _loop;
            }
        }
    }

    public IReadOnlyList<WatchedStock> Watchlist
    {
        get
        {
            lock (_lock)
            {
                return [.. LoadWatchlist()];
            }
        }
    }

    public StockQuote? GetQuote(string symbol)
    {
        lock (_lock)
        {
            return _quotes.GetValueOrDefault(symbol);
        }
    }

    private static string Normalize(string symbol) => symbol.Trim().ToUpperInvariant();

    #region Watchlist storage

    /// <summary>Reads the list on first use. Caller holds the lock.</summary>
    private List<WatchedStock> LoadWatchlist()
    {
        if (_watchlist is not null)
        {
            return _watchlist;
        }

        var stored = _settingsService.Get(WatchlistKey);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            try
            {
                _watchlist = JsonSerializer.Deserialize<List<WatchedStock>>(stored)?
                    .Where(stock => !string.IsNullOrWhiteSpace(stock?.Symbol))
                    .ToList();
            }
            catch (JsonException exception)
            {
                _logger?.Warning(exception, "The stock watchlist could not be read; starting from the defaults");
            }
        }

        // Never stored (or unreadable): the defaults. An emptied list is stored as "[]" and stays empty.
        _watchlist ??= [.. Defaults];
        return _watchlist;
    }

    /// <summary>Caller holds the lock.</summary>
    private void SaveWatchlist() =>
        _settingsService.Set(WatchlistKey, JsonSerializer.Serialize(_watchlist ?? []));

    public async Task<WatchedStock> AddAsync(string symbol, string? name = null, CancellationToken cancellationToken = default)
    {
        symbol = Normalize(symbol);
        if (symbol.Length == 0)
        {
            throw new InvalidOperationException("Type a symbol or a company name.");
        }

        lock (_lock)
        {
            if (LoadWatchlist().Any(stock => string.Equals(stock.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"{symbol} is already on the list.");
            }
        }

        // Asked before it is kept: a typo would otherwise sit on the list as a row that never fills in.
        var quotes = await _provider.GetQuotesAsync([symbol], cancellationToken);
        if (!quotes.TryGetValue(symbol, out var quote))
        {
            throw new InvalidOperationException($"No quote for {symbol}. Check the symbol; other exchanges add a suffix, such as VOD.L.");
        }

        var stock = new WatchedStock(symbol, string.IsNullOrWhiteSpace(name) ? quote.Name : name.Trim());
        lock (_lock)
        {
            var list = LoadWatchlist();
            if (list.Any(existing => string.Equals(existing.Symbol, symbol, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"{symbol} is already on the list.");
            }

            list.Add(stock);
            _quotes[symbol] = quote;
            SaveWatchlist();
        }

        RaiseChanged();
        return stock;
    }

    public bool Remove(string symbol)
    {
        symbol = Normalize(symbol);
        lock (_lock)
        {
            var list = LoadWatchlist();
            if (list.RemoveAll(stock => string.Equals(stock.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return false;
            }

            _quotes.Remove(symbol);
            SaveWatchlist();
        }

        RaiseChanged();
        return true;
    }

    #endregion

    #region Quotes

    public Task<IReadOnlyList<StockSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        _provider.SearchAsync(query, cancellationToken);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        string[] symbols;
        lock (_lock)
        {
            symbols = [.. LoadWatchlist().Select(stock => stock.Symbol)];
        }

        if (symbols.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, StockQuote> quotes;
        try
        {
            quotes = await _provider.GetQuotesAsync(symbols, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.Warning(exception, "Fetching stock quotes failed");
            SetOutcome(exception.Message, updated: false);
            return;
        }

        lock (_lock)
        {
            // A stock removed while the request was out must not come back through its quote.
            var watched = LoadWatchlist().Select(stock => stock.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (symbol, quote) in quotes)
            {
                if (watched.Contains(symbol))
                {
                    _quotes[symbol] = quote;
                }
            }
        }

        SetOutcome(quotes.Count == 0 ? "No quotes received: the quote source cannot be reached." : string.Empty, updated: quotes.Count > 0);
    }

    private void SetOutcome(string error, bool updated)
    {
        lock (_lock)
        {
            _lastError = error;
            if (updated)
            {
                _lastUpdated = DateTime.Now;
            }
        }

        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public IDisposable Watch()
    {
        lock (_lock)
        {
            if (++_watchers == 1)
            {
                var cancellation = new CancellationTokenSource();
                _loopCancellation = cancellation;
                _loop = Task.Run(() => RunLoopAsync(cancellation.Token));
            }
        }

        return new Watcher(this);
    }

    private void StopWatching()
    {
        lock (_lock)
        {
            if (_watchers == 0 || --_watchers > 0)
            {
                return;
            }

            _loopCancellation?.Cancel();
            _loopCancellation = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_refreshInterval);
            do
            {
                // A failing refresh is tolerated and tried again on the next tick.
                await RefreshAsync(cancellationToken);
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            // The last panel left.
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _watchers = 0;
            _loopCancellation?.Cancel();
            _loopCancellation = null;
        }
    }

    private sealed class Watcher(StockService owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.StopWatching();
            }
        }
    }

    #endregion
}
