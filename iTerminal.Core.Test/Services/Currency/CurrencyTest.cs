using iTerminal.Core.Services;
using iTerminal.Core.Services.Currency;

namespace iTerminal.Core.Test.Services.Currency;

[TestClass]
public class CurrencyTest
{
    internal sealed class MemorySettings : ISettingsService
    {
        public Dictionary<string, string> Values { get; } = [];
        public bool IsDirty => false;
        public string? Get(string key) => Values.GetValueOrDefault(key);
        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
        public void Set(string key, string value) => Values[key] = value;
        public void Write() { }
    }

    internal sealed class FakeProvider(CurrencyRates? rates = null, Exception? failure = null) : ICurrencyRateProvider
    {
        public int Calls { get; private set; }
        public string Name => "Fake";

        /// <summary>Holds the download until this task completes; with <see cref="IgnoresCancellation"/> the cancellation is only reported then.</summary>
        public Task? Gate { get; set; }

        public bool IgnoresCancellation { get; set; }

        public Task<CurrencyRates> FetchAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Gate is not null)
            {
                return AfterGate(cancellationToken);
            }

            return failure is not null ? Task.FromException<CurrencyRates>(failure) : Task.FromResult(rates ?? Sample());
        }

        private async Task<CurrencyRates> AfterGate(CancellationToken cancellationToken)
        {
            if (IgnoresCancellation)
            {
                await Gate!;
                cancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                await Gate!.WaitAsync(cancellationToken);
            }

            return rates ?? Sample();
        }
    }

    internal static CurrencyRates Sample() =>
        new(new DateOnly(2026, 10, 16), new Dictionary<string, decimal> { ["USD"] = 2m, ["GBP"] = 0.5m });

    private const string EcbXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
          <gesmes:subject>Reference rates</gesmes:subject>
          <Cube>
            <Cube time="2026-10-16">
              <Cube currency="USD" rate="1.0823"/>
              <Cube currency="JPY" rate="162.1"/>
              <Cube currency="BAD" rate="x"/>
            </Cube>
          </Cube>
        </gesmes:Envelope>
        """;

    [TestMethod]
    public void ConvertsAnyPairThroughTheEuro()
    {
        CurrencyRates rates = Sample();

        Assert.AreEqual(200m, rates.Convert(100m, "EUR", "USD"));
        Assert.AreEqual(50m, rates.Convert(100m, "USD", "EUR"));
        Assert.AreEqual(25m, rates.Convert(100m, "USD", "GBP"));
        Assert.AreEqual(100m, rates.Convert(100m, "eur", "EUR"), "codes are not case sensitive");
    }

    [TestMethod]
    public void AnUnknownCurrencyHasNoRate()
    {
        Assert.IsNull(Sample().Convert(1m, "EUR", "XYZ"));
        Assert.IsNull(Sample().RateBetween("XYZ", "EUR"));
    }

    [TestMethod]
    public void TheCodesIncludeTheEuroAndAreSorted()
    {
        CollectionAssert.AreEqual(new[] { "EUR", "GBP", "USD" }, Sample().Codes.ToArray());
    }

    [TestMethod]
    public void RatesSurviveSerialization()
    {
        CurrencyRates? back = CurrencyRates.TryParse(Sample().Serialize());

        Assert.IsNotNull(back);
        Assert.AreEqual(new DateOnly(2026, 10, 16), back.Date);
        Assert.AreEqual(2m, back.PerEuro["USD"]);
        Assert.AreEqual(0.5m, back.PerEuro["GBP"]);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("not a date;USD=1")]
    [DataRow("2026-10-16")]
    [DataRow("2026-10-16;USD=zero")]
    public void GarbageIsNotRates(string? text)
    {
        Assert.IsNull(CurrencyRates.TryParse(text));
    }

    [TestMethod]
    public void TheEcbFileIsReadAndBadRowsAreSkipped()
    {
        CurrencyRates rates = EcbCurrencyRateProvider.Parse(EcbXml);

        Assert.AreEqual(new DateOnly(2026, 10, 16), rates.Date);
        Assert.AreEqual(1.0823m, rates.PerEuro["USD"]);
        Assert.AreEqual(162.1m, rates.PerEuro["JPY"]);
        Assert.IsFalse(rates.PerEuro.ContainsKey("BAD"));
    }

    [TestMethod]
    [DataRow("<html/>")]
    [DataRow("this is not xml")]
    public void ANonRatesFileIsRefusedInWords(string xml)
    {
        Assert.ThrowsExactly<CurrencyException>(() => EcbCurrencyRateProvider.Parse(xml));
    }

    [TestMethod]
    public async Task RefreshingKeepsTheRatesForTheNextRun()
    {
        var settings = new MemorySettings();
        var service = new CurrencyService(new FakeProvider(), settings);
        Assert.IsNull(service.Current);
        Assert.IsTrue(service.IsStale);

        await service.RefreshAsync();

        Assert.IsFalse(service.IsStale);
        Assert.AreEqual(2m, service.Current!.PerEuro["USD"]);
        var nextRun = new CurrencyService(new FakeProvider(), settings);
        Assert.AreEqual(2m, nextRun.Current!.PerEuro["USD"], "the kept rates work offline");
        Assert.IsTrue(nextRun.IsStale, "kept rates are shown but still refreshed");
    }

    [TestMethod]
    public async Task AFailedDownloadKeepsTheOldRates()
    {
        var settings = new MemorySettings();
        settings.Values[CurrencyService.RatesKey] = Sample().Serialize();
        var service = new CurrencyService(new FakeProvider(failure: new CurrencyException("offline")), settings);

        await Assert.ThrowsExactlyAsync<CurrencyException>(() => service.RefreshAsync());

        Assert.AreEqual(2m, service.Current!.PerEuro["USD"]);
    }
}
