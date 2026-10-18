using System.Globalization;

namespace iTerminal.Core.Services.Currency;

/// <summary>
/// A day's exchange rates, every one quoted per one euro (the reference currency of the European Central Bank's
/// daily rates). Any pair is converted through the euro, so one table serves all the currencies in it.
/// </summary>
public sealed record CurrencyRates(DateOnly Date, IReadOnlyDictionary<string, decimal> PerEuro)
{
    public const string Euro = "EUR";

    /// <summary>The currency codes the table can convert, the euro included, in alphabetical order.</summary>
    public IReadOnlyList<string> Codes =>
        [.. PerEuro.Keys.Append(Euro).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal)];

    public bool Contains(string code) => RateOf(code) is not null;

    /// <summary>The amount of <paramref name="to"/> that <paramref name="amount"/> of <paramref name="from"/> buys; null when either is unknown.</summary>
    public decimal? Convert(decimal amount, string from, string to)
    {
        if (RateOf(from) is not { } fromRate || RateOf(to) is not { } toRate)
        {
            return null;
        }

        return amount / fromRate * toRate;
    }

    /// <summary>How much of <paramref name="to"/> one <paramref name="from"/> buys.</summary>
    public decimal? RateBetween(string from, string to) => Convert(1m, from, to);

    private decimal? RateOf(string code)
    {
        if (string.Equals(code, Euro, StringComparison.OrdinalIgnoreCase))
        {
            return 1m;
        }

        foreach (KeyValuePair<string, decimal> pair in PerEuro)
        {
            if (string.Equals(pair.Key, code, StringComparison.OrdinalIgnoreCase) && pair.Value > 0)
            {
                return pair.Value;
            }
        }

        return null;
    }

    /// <summary>One line, for the settings table: <c>2026-10-16;USD=1.0823;JPY=162.1</c>.</summary>
    public string Serialize() =>
        string.Join(';', PerEuro.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")).Prepend(Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    public static CurrencyRates? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] parts = text.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            return null;
        }

        Dictionary<string, decimal> rates = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in parts.Skip(1))
        {
            string[] pair = part.Split('=');
            if (pair.Length == 2 && decimal.TryParse(pair[1], NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate) && rate > 0)
            {
                rates[pair[0].ToUpperInvariant()] = rate;
            }
        }

        return rates.Count == 0 ? null : new CurrencyRates(date, rates);
    }
}
