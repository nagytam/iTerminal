using System.Globalization;

namespace iTerminal.Core.Services.Portfolio;

public class StooqQuoteProvider : IQuoteProvider
{
    private static readonly HttpClient _httpClient = new();

    public async Task<IReadOnlyDictionary<string, StooqQuote>> GetQuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellationToken = default)
    {
        if (symbols.Count == 0)
        {
            return new Dictionary<string, StooqQuote>();
        }

        var joined = string.Join(",", symbols);
        var url = $"https://stooq.com/q/l/?s={Uri.EscapeDataString(joined)}&f=sd2t2ohlcv&h&e=csv";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var csv = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseCsv(csv);
    }

    internal static IReadOnlyDictionary<string, StooqQuote> ParseCsv(string csv)
    {
        var result = new Dictionary<string, StooqQuote>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv))
        {
            return result;
        }

        var lines = csv.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = lines[i].Split(',');
            if (fields.Length < 7)
            {
                continue;
            }

            var symbol = fields[0].Trim();
            var date = fields[1].Trim();
            var time = fields[2].Trim();
            var close = fields[6].Trim();

            if (symbol.Length == 0 || close.Equals("N/D", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!decimal.TryParse(close, NumberStyles.Number, CultureInfo.InvariantCulture, out var closeValue))
            {
                continue;
            }

            if (!DateTime.TryParse($"{date} {time}", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
            {
                timestamp = DateTime.UtcNow;
            }

            result[symbol] = new StooqQuote(symbol, closeValue, timestamp);
        }

        return result;
    }
}
