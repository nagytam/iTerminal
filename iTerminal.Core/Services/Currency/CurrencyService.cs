namespace iTerminal.Core.Services.Currency;

public interface ICurrencyService
{
    /// <summary>The rates last downloaded (or kept from an earlier run); null before the first download.</summary>
    CurrencyRates? Current { get; }

    /// <summary>True when there are no rates or they were not downloaded in the last <see cref="CurrencyService.MaxAge"/> of this run.</summary>
    bool IsStale { get; }

    /// <summary>Downloads the rates, remembers them for the next run and returns them.</summary>
    Task<CurrencyRates> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Holds the rates: downloaded on demand, kept in the settings so the converter works offline with the last ones, and
/// never replaced by a failed download.
/// </summary>
public sealed class CurrencyService : ICurrencyService
{
    public const string RatesKey = "CurrencyConverter.Rates";
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);

    private readonly ICurrencyRateProvider _provider;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private DateTimeOffset? _downloadedAt;

    public CurrencyService(ICurrencyRateProvider provider, ISettingsService settings, TimeProvider? time = null)
    {
        _provider = provider;
        _settings = settings;
        _time = time ?? TimeProvider.System;
        Current = CurrencyRates.TryParse(settings.Get(RatesKey));
    }

    public CurrencyRates? Current { get; private set; }

    public bool IsStale => Current is null || _downloadedAt is not { } at || _time.GetUtcNow() - at > MaxAge;

    public async Task<CurrencyRates> RefreshAsync(CancellationToken cancellationToken = default)
    {
        CurrencyRates rates = await _provider.FetchAsync(cancellationToken);
        Current = rates;
        _downloadedAt = _time.GetUtcNow();
        _settings.Set(RatesKey, rates.Serialize());
        return rates;
    }
}
