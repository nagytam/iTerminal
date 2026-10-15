using iTerminal.Core.Data;
using iTerminal.Core.Jobs;
using iTerminal.Core.Services;
using Microsoft.Playwright;

namespace iTerminal.Core.Test.Jobs;

[TestClass]
public class OnlineQuoteJobTest
{
    private static IPlaywright? _playwright;
    private static IBrowser? _browser;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await LaunchChromiumAsync(_playwright);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }
        _playwright?.Dispose();
    }

    [TestMethod]
    [TestCategory("Integration")]
    [Timeout(120_000)]
    public async Task ParseQuotePageAsync_FetchesBitcoinQuote_FromSwissquote()
    {
        var service = new OnlineQuoteService();
        var cacheService = new MockCacheService();
        var job = new OnlineQuoteJob(service, cacheService);

        var page = await _browser!.NewPageAsync();
        try
        {
            await page.GotoAsync(OnlineQuoteJob.BTCUrl);
            await job.ParseQuotePageAsync(page);
        }
        finally
        {
            await page.CloseAsync();
        }

        Assert.AreEqual(1, service.Instruments.Count, "Expected exactly one instrument to be added.");
        var instrument = service.Instruments.Values.Single();
        Assert.IsFalse(string.IsNullOrWhiteSpace(instrument.Name), "Instrument name should be populated.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(instrument.Currency), "Instrument currency should be populated.");
        Assert.AreEqual(1, instrument.InstrumentValues.Count, "Expected exactly one instrument value.");
        var value = instrument.InstrumentValues[0];
        Assert.IsTrue(value.Value > 0m, $"Expected a positive BTC price, got {value.Value}.");
        Assert.AreEqual(instrument.Id, value.InstrumentId);
        Assert.AreEqual(nameof(OnlineQuoteJob), value.Source);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [Timeout(120_000)]
    public async Task ParseQuotePageAsync_FetchesEthereumQuote_FromSwissquote()
    {
        var service = new OnlineQuoteService();
        var cacheService = new MockCacheService();
        var job = new OnlineQuoteJob(service, cacheService);

        var page = await _browser!.NewPageAsync();
        try
        {
            await page.GotoAsync(OnlineQuoteJob.ETHUrl);
            await job.ParseQuotePageAsync(page);
        }
        finally
        {
            await page.CloseAsync();
        }

        Assert.AreEqual(1, service.Instruments.Count, "Expected exactly one instrument to be added.");
        var instrument = service.Instruments.Values.Single();
        Assert.IsFalse(string.IsNullOrWhiteSpace(instrument.Name), "Instrument name should be populated.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(instrument.Currency), "Instrument currency should be populated.");
        Assert.AreEqual(1, instrument.InstrumentValues.Count, "Expected exactly one instrument value.");
        var value = instrument.InstrumentValues[0];
        Assert.IsTrue(value.Value > 0m, $"Expected a positive ETH price, got {value.Value}.");
        Assert.AreEqual(instrument.Id, value.InstrumentId);
        Assert.AreEqual(nameof(OnlineQuoteJob), value.Source);
    }

    [TestMethod]
    [TestCategory("Integration")]
    [Timeout(180_000)]
    public async Task ParseQuotePageAsync_FetchesBothQuotes_AccumulatesInService()
    {
        var service = new OnlineQuoteService();
        var cacheService = new MockCacheService();
        var job = new OnlineQuoteJob(service, cacheService);

        foreach (var url in new[] { OnlineQuoteJob.BTCUrl, OnlineQuoteJob.ETHUrl })
        {
            var page = await _browser!.NewPageAsync();
            try
            {
                await page.GotoAsync(url);
                await job.ParseQuotePageAsync(page);
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        Assert.AreEqual(2, service.Instruments.Count, "Expected both BTC and ETH instruments after sequential fetches.");
        foreach (var instrument in service.Instruments.Values)
        {
            Assert.AreEqual(1, instrument.InstrumentValues.Count);
            Assert.IsTrue(instrument.InstrumentValues[0].Value > 0m);
        }
    }

    private class MockCacheService : ICacheService
    {
        public Task<CachedWeatherInfo?> GetCachedWeatherAsync() => Task.FromResult<CachedWeatherInfo?>(null);
        public Task SetCachedWeatherAsync(WeatherInfo weatherInfo) => Task.CompletedTask;
        public Task<List<CachedInstrumentValue>> GetCachedInstrumentsAsync() => Task.FromResult(new List<CachedInstrumentValue>());
        public Task SetCachedInstrumentAsync(string instrumentName, string currency, decimal value, int instrumentType, int instrumentValueType, DateTime sourceDateTime) => Task.CompletedTask;
        public Task ClearCachedInstrumentsAsync() => Task.CompletedTask;
    }

    private static async Task<IBrowser> LaunchChromiumAsync(IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
            });
        }
        catch (PlaywrightException)
        {
            var installExitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
            if (installExitCode == 0)
            {
                return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                });
            }

            foreach (var candidate in new[] { "/snap/bin/chromium", "/usr/bin/chromium", "/usr/bin/chromium-browser", "/usr/bin/google-chrome" })
            {
                if (File.Exists(candidate))
                {
                    return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        ExecutablePath = candidate,
                    });
                }
            }

            Assert.Fail($"Playwright browser install failed (exit code {installExitCode}) and no system Chromium found.");
            throw new InvalidOperationException($"Playwright browser install failed (exit code {installExitCode}) and no system Chromium found.");
        }
    }
}
