namespace iTerminal.Core.Services.Currency;

/// <summary>English names for the currencies the daily rates list; an unlisted code is shown as itself.</summary>
public static class CurrencyNames
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUD"] = "Australian dollar", ["BGN"] = "Bulgarian lev", ["BRL"] = "Brazilian real", ["CAD"] = "Canadian dollar",
        ["CHF"] = "Swiss franc", ["CNY"] = "Chinese yuan", ["CZK"] = "Czech koruna", ["DKK"] = "Danish krone",
        ["EUR"] = "Euro", ["GBP"] = "Pound sterling", ["HKD"] = "Hong Kong dollar", ["HUF"] = "Hungarian forint",
        ["IDR"] = "Indonesian rupiah", ["ILS"] = "Israeli shekel", ["INR"] = "Indian rupee", ["ISK"] = "Icelandic króna",
        ["JPY"] = "Japanese yen", ["KRW"] = "South Korean won", ["MXN"] = "Mexican peso", ["MYR"] = "Malaysian ringgit",
        ["NOK"] = "Norwegian krone", ["NZD"] = "New Zealand dollar", ["PHP"] = "Philippine peso", ["PLN"] = "Polish złoty",
        ["RON"] = "Romanian leu", ["SEK"] = "Swedish krona", ["SGD"] = "Singapore dollar", ["THB"] = "Thai baht",
        ["TRY"] = "Turkish lira", ["USD"] = "US dollar", ["ZAR"] = "South African rand",
    };

    public static string NameOf(string code) => Names.GetValueOrDefault(code, code);
}
