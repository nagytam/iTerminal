using iTerminal.Core.Data;
using iTerminal.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace iTerminal.Core.Services.Test;

/// <summary>The cache behind the menu bar: a reading is served only while it is fresh, and each quote is judged on its own age.</summary>
[TestClass]
public class CacheServiceTest
{
    private sealed class Factory(DbContextOptions<iTerminalDbContext> options) : IDbContextFactory<iTerminalDbContext>
    {
        public iTerminalDbContext CreateDbContext() => new(options);
        public Task<iTerminalDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private string _path = string.Empty;
    private DbContextOptions<iTerminalDbContext> _options = null!;

    [TestInitialize]
    public void Initialize()
    {
        _path = Path.Combine(Path.GetTempPath(), "CacheServiceTest_" + Guid.NewGuid().ToString("N") + ".sqlite");
        _options = new DbContextOptionsBuilder<iTerminalDbContext>().UseSqlite($"Data Source={_path}").Options;
        using var context = new iTerminalDbContext(_options);
        context.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            File.Delete(_path);
        }
        catch
        {
        }
    }

    private CacheService Cache() => new(new Factory(_options));

    private void SeedInstrument(string name, DateTime cachedAtUtc)
    {
        using var context = new iTerminalDbContext(_options);
        context.CachedInstrumentValues.Add(new CachedInstrumentValue
        {
            InstrumentName = name,
            Currency = "USD",
            Value = 1m,
            SourceDateTime = cachedAtUtc,
            CachedAtUtc = cachedAtUtc,
        });
        context.SaveChanges();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFreshQuote_IsServed()
    {
        SeedInstrument("BTC", DateTime.UtcNow);

        var quotes = await Cache().GetCachedInstrumentsAsync();

        CollectionAssert.AreEqual(new[] { "BTC" }, quotes.Select(q => q.InstrumentName).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AQuoteOlderThanTheTtl_IsNotServed()
    {
        SeedInstrument("ETH", DateTime.UtcNow.AddHours(-2));

        var quotes = await Cache().GetCachedInstrumentsAsync();

        Assert.AreEqual(0, quotes.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnOldQuoteBesideAFreshOne_IsLeftOut_NotServedAsCurrent()
    {
        // The first entry is fresh; the second is hours old and must not ride along as if it were current.
        SeedInstrument("BTC", DateTime.UtcNow);
        SeedInstrument("ETH", DateTime.UtcNow.AddHours(-2));

        var quotes = await Cache().GetCachedInstrumentsAsync();

        CollectionAssert.AreEqual(new[] { "BTC" }, quotes.Select(q => q.InstrumentName).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASetWeatherReading_IsReadBack()
    {
        var cache = Cache();

        await cache.SetCachedWeatherAsync(new iTerminal.Core.Data.WeatherInfo { LocationName = "Oslo", TemperatureC = -3 });

        var reading = await cache.GetCachedWeatherAsync();
        Assert.AreEqual("Oslo", reading!.LocationName);
    }
}
