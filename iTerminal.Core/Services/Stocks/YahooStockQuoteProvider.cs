using System.Globalization;
using System.Text.Json;

namespace iTerminal.Core.Services.Stocks;

/// <summary>
/// Quotes and symbol search from Yahoo Finance's public JSON endpoints: no key, one request per
/// symbol for quotes (the chart endpoint is the one that still answers without a login), and the
/// search endpoint for finding a stock on any of the exchanges Yahoo lists. Symbols are Yahoo's:
/// a bare ticker for the US, a suffix for the rest (<c>VOD.L</c> London, <c>SAP.DE</c> Xetra).
/// These are unofficial endpoints, so every failure is contained: a symbol that cannot be reached
/// is left out of the answer, and the caller keeps showing the quote it had.
/// </summary>
public sealed class YahooStockQuoteProvider : IStockQuoteProvider
{
    private const string Host = "https://query1.finance.yahoo.com";
    private const int MaxParallelRequests = 4;

    private static readonly HttpClient SharedClient = CreateClient();

    private readonly HttpClient _client;

    public YahooStockQuoteProvider()
        : this(SharedClient)
    {
    }

    internal YahooStockQuoteProvider(HttpClient client)
    {
        _client = client;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // The endpoints refuse a request with no browser-like agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; iTerminal)");
        return client;
    }

    public async Task<IReadOnlyDictionary<string, StockQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, StockQuote>(StringComparer.OrdinalIgnoreCase);
        using var gate = new SemaphoreSlim(MaxParallelRequests);
        var tasks = symbols.Select(async symbol =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var quote = await GetQuoteAsync(symbol, cancellationToken);
                if (quote is not null)
                {
                    lock (result)
                    {
                        result[symbol] = quote;
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return result;
    }

    private async Task<StockQuote?> GetQuoteAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{Host}/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=1d";
            using var response = await _client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return ParseChart(symbol, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<StockSearchHit>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var url = $"{Host}/v1/finance/search?q={Uri.EscapeDataString(query.Trim())}&quotesCount=12&newsCount=0";
        using var response = await _client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ParseSearch(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Reads the first result of a chart response; null when the source has no data for the symbol.</summary>
    internal static StockQuote? ParseChart(string symbol, string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("chart", out var chart)
            || !chart.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0
            || !results[0].TryGetProperty("meta", out var meta))
        {
            return null;
        }

        var price = Decimal(meta, "regularMarketPrice");
        if (price is null)
        {
            return null;
        }

        var name = String(meta, "longName") ?? String(meta, "shortName") ?? symbol;
        DateTime? marketTime = meta.TryGetProperty("regularMarketTime", out var time) && time.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
            : null;

        return new StockQuote(
            symbol,
            name.Trim(),
            price.Value,
            Decimal(meta, "chartPreviousClose") ?? Decimal(meta, "previousClose") ?? price.Value,
            String(meta, "currency") ?? string.Empty,
            String(meta, "fullExchangeName") ?? String(meta, "exchangeName") ?? string.Empty,
            marketTime,
            Decimal(meta, "regularMarketDayHigh"),
            Decimal(meta, "regularMarketDayLow"),
            Decimal(meta, "fiftyTwoWeekHigh"),
            Decimal(meta, "fiftyTwoWeekLow"),
            meta.TryGetProperty("regularMarketVolume", out var volume) && volume.TryGetInt64(out var shares) ? shares : null);
    }

    /// <summary>Reads a search response; only listed stocks (and ETFs, which trade the same way) are kept.</summary>
    internal static IReadOnlyList<StockSearchHit> ParseSearch(string json)
    {
        // An empty body or an error page is no hits, not an exception into the dialog that asked.
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return ParseHits(json);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<StockSearchHit> ParseHits(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("quotes", out var quotes) || quotes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<StockSearchHit> hits = [];
        foreach (var quote in quotes.EnumerateArray())
        {
            var symbol = String(quote, "symbol");
            var type = String(quote, "quoteType") ?? string.Empty;
            var isStock = type.Equals("EQUITY", StringComparison.OrdinalIgnoreCase) || type.Equals("ETF", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(symbol) || !isStock)
            {
                continue;
            }

            var name = (String(quote, "longname") ?? String(quote, "shortname") ?? symbol).Trim();
            hits.Add(new StockSearchHit(symbol, name, String(quote, "exchDisp") ?? String(quote, "exchange") ?? string.Empty, type));
        }

        return hits;
    }

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static decimal? Decimal(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            && decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
