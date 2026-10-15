using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services;

public interface ICacheService
{
    Task<CachedWeatherInfo?> GetCachedWeatherAsync();
    Task SetCachedWeatherAsync(WeatherInfo weatherInfo);
    Task<List<CachedInstrumentValue>> GetCachedInstrumentsAsync();
    Task SetCachedInstrumentAsync(string instrumentName, string currency, decimal value, int instrumentType, int instrumentValueType, DateTime sourceDateTime);
    Task ClearCachedInstrumentsAsync();
}

public class CacheService(IDbContextFactory<iTerminalDbContext> dbContextFactory) : ICacheService
{
    private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory = dbContextFactory;

    /// <summary>
    /// Weather cache TTL: 15 minutes (matching the refresh interval in WeatherJob)
    /// </summary>
    private static readonly TimeSpan WeatherCacheTtl = TimeSpan.FromMinutes(15);

    public async Task<CachedWeatherInfo?> GetCachedWeatherAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var cached = await dbContext.CachedWeatherInfos
            .OrderByDescending(w => w.CachedAtUtc)
            .FirstOrDefaultAsync();

        // Return null if cache is expired, allowing fresh data to be fetched
        if (cached != null && DateTime.UtcNow - cached.CachedAtUtc > WeatherCacheTtl)
        {
            return null;
        }

        return cached;
    }

    public async Task SetCachedWeatherAsync(WeatherInfo weatherInfo)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // Remove old entries efficiently using ExecuteDeleteAsync
        await dbContext.CachedWeatherInfos.ExecuteDeleteAsync();

        // Add new entry
        var cachedWeather = new CachedWeatherInfo
        {
            LocationName = weatherInfo.LocationName,
            TemperatureC = weatherInfo.TemperatureC,
            WeatherCode = weatherInfo.WeatherCode,
            IsDay = weatherInfo.IsDay,
            SourceDateTime = weatherInfo.SourceDateTime,
            CachedAtUtc = DateTime.UtcNow
        };

        dbContext.CachedWeatherInfos.Add(cachedWeather);
        await dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Instrument/Quote cache TTL: 5 minutes
    /// </summary>
    private static readonly TimeSpan InstrumentCacheTtl = TimeSpan.FromMinutes(5);

    public async Task<List<CachedInstrumentValue>> GetCachedInstrumentsAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        var cached = await dbContext.CachedInstrumentValues
            .GroupBy(i => i.InstrumentName)
            .Select(g => g.OrderByDescending(i => i.CachedAtUtc).First())
            .ToListAsync();

        // Return empty list if cache is expired, allowing fresh data to be fetched
        if (cached.Count > 0 && DateTime.UtcNow - cached[0].CachedAtUtc > InstrumentCacheTtl)
        {
            return [];
        }

        return cached;
    }

    public async Task SetCachedInstrumentAsync(string instrumentName, string currency, decimal value, int instrumentType, int instrumentValueType, DateTime sourceDateTime)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

        // Remove old entries for this instrument efficiently
        await dbContext.CachedInstrumentValues
            .Where(i => i.InstrumentName == instrumentName)
            .ExecuteDeleteAsync();

        var cachedInstrument = new CachedInstrumentValue
        {
            InstrumentName = instrumentName,
            Currency = currency,
            Value = value,
            InstrumentType = instrumentType,
            InstrumentValueType = instrumentValueType,
            SourceDateTime = sourceDateTime,
            CachedAtUtc = DateTime.UtcNow
        };

        dbContext.CachedInstrumentValues.Add(cachedInstrument);
        await dbContext.SaveChangesAsync();
    }

    public async Task ClearCachedInstrumentsAsync()
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        await dbContext.CachedInstrumentValues.ExecuteDeleteAsync();
    }
}
