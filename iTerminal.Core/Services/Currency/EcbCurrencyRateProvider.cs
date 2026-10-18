using System.Globalization;
using System.Xml.Linq;

namespace iTerminal.Core.Services.Currency;

/// <summary>
/// The European Central Bank's daily reference rates: one small XML file, no key, about thirty currencies quoted per
/// euro, published on working days around 16:00 CET.
/// </summary>
public sealed class EcbCurrencyRateProvider : ICurrencyRateProvider
{
    public const string DefaultUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly HttpClient _client;
    private readonly string _url;

    public EcbCurrencyRateProvider()
        : this(SharedClient, DefaultUrl)
    {
    }

    internal EcbCurrencyRateProvider(HttpClient client, string url)
    {
        _client = client;
        _url = url;
    }

    public string Name => "European Central Bank";

    public async Task<CurrencyRates> FetchAsync(CancellationToken cancellationToken = default)
    {
        string xml;
        try
        {
            xml = await _client.GetStringAsync(_url, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new CurrencyException($"Could not download the rates from {Name}: {exception.Message}", exception);
        }

        return Parse(xml);
    }

    /// <summary>Reads the <c>&lt;Cube time="…"&gt;&lt;Cube currency="USD" rate="1.08"/&gt;…</c> tree, whatever namespace it carries.</summary>
    internal static CurrencyRates Parse(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new CurrencyException("The rates file could not be read.", exception);
        }

        XElement? day = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Cube" && element.Attribute("time") is not null);
        if (day is null || !DateOnly.TryParseExact((string?)day.Attribute("time"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            throw new CurrencyException("The rates file has no dated rates.");
        }

        Dictionary<string, decimal> rates = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement cube in day.Elements().Where(element => element.Name.LocalName == "Cube"))
        {
            if ((string?)cube.Attribute("currency") is { Length: 3 } code
                && decimal.TryParse((string?)cube.Attribute("rate"), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal rate)
                && rate > 0)
            {
                rates[code.ToUpperInvariant()] = rate;
            }
        }

        return rates.Count == 0 ? throw new CurrencyException("The rates file lists no currencies.") : new CurrencyRates(date, rates);
    }
}
