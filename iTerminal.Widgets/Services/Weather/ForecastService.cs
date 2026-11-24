using iTerminal.Core.Services;
using System.Text.Json;
using Serilog;

namespace iTerminal.Widgets.Services.Weather;

/// <summary>
/// The saved places, the home place and the forecast for each place. A forecast is kept in the settings so the dialog shows
/// the last one offline; a failed download never replaces it. Nothing here waits on the network in a caller's thread: the dialog
/// starts <see cref="RefreshAsync"/> on a background task and reads the cache back.
/// </summary>
public interface IForecastService
{
    /// <summary>Raised from a background thread when the places, the home place or a forecast changed.</summary>
    event EventHandler? Changed;

    /// <summary>The saved places, in the order the user keeps them.</summary>
    IReadOnlyList<SavedPlace> Places { get; }

    /// <summary>The home place, or null when none is set.</summary>
    SavedPlace? Home { get; }

    /// <summary>The last forecast for <paramref name="place"/>, from this run or an earlier one; null when there is none.</summary>
    Forecast? GetForecast(SavedPlace place);

    /// <summary>
    /// True when the place has no forecast, or its forecast is older than <see cref="ForecastService.MaxAge"/>, and no attempt was made within
    /// that time. A failed attempt therefore waits <see cref="ForecastService.MaxAge"/> before the next, even with no forecast kept.
    /// </summary>
    bool NeedsRefresh(SavedPlace place);

    /// <summary>
    /// Downloads the forecast, keeps it for the next run and returns it. Throws when it cannot be had; the kept forecast stays.
    /// Only the newest request for a place may land, so an older one that finishes late is dropped.
    /// </summary>
    Task<Forecast> RefreshAsync(SavedPlace place, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaceHit>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Saves a place. False when it is already saved.</summary>
    bool AddPlace(SavedPlace place);

    /// <summary>Removes a place with its forecast; a home place that is removed leaves no home. False when it was not saved.</summary>
    bool RemovePlace(SavedPlace place);

    /// <summary>Makes a saved place the home place.</summary>
    void SetHome(SavedPlace place);
}

public sealed class ForecastService : IForecastService
{
    public const string PlacesKey = "Weather.Places";
    public const string HomeKey = "Weather.Home";
    public const string ForecastsKey = "Weather.Forecast";

    /// <summary>How old a forecast may be before the dialog asks for a new one.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ISettingsService _settings;
    private readonly IForecastProvider _provider;
    private readonly TimeProvider _time;
    private readonly ILogger? _logger;
    private readonly Lock _lock = new();
    private readonly Lock _saveLock = new();
    private readonly Dictionary<string, Forecast> _forecasts;
    private readonly Dictionary<string, DateTimeOffset> _lastAttempts = [];
    private readonly Dictionary<string, long> _requestSequence = [];
    private readonly List<SavedPlace> _places;
    private string? _homeKey;

    public ForecastService(ISettingsService settings, IForecastProvider provider, TimeProvider? time = null, ILogger? logger = null)
    {
        _settings = settings;
        _provider = provider;
        _time = time ?? TimeProvider.System;
        _logger = logger;
        // A value that parses but is missing parts is dropped here, so the dialog never draws from a forecast it cannot read.
        _places = (Load<List<SavedPlace>>(PlacesKey) ?? []).Where(place => place is not null).ToList();
        _forecasts = (Load<Dictionary<string, Forecast>>(ForecastsKey) ?? [])
            .Where(entry => entry.Value is { Current: not null, Hourly: not null, Daily: not null })
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        _homeKey = settings.Get(HomeKey);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<SavedPlace> Places
    {
        get
        {
            lock (_lock)
            {
                return [.. _places];
            }
        }
    }

    public SavedPlace? Home
    {
        get
        {
            lock (_lock)
            {
                return _places.FirstOrDefault(place => place.Key == _homeKey);
            }
        }
    }

    public Forecast? GetForecast(SavedPlace place)
    {
        lock (_lock)
        {
            return _forecasts.GetValueOrDefault(place.Key);
        }
    }

    public bool NeedsRefresh(SavedPlace place)
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var attempted = _lastAttempts.GetValueOrDefault(place.Key);
            var fresh = _forecasts.TryGetValue(place.Key, out var forecast) && now - forecast.FetchedAt <= MaxAge;
            return !fresh && (attempted == default || now - attempted > MaxAge);
        }
    }

    public async Task<Forecast> RefreshAsync(SavedPlace place, CancellationToken cancellationToken = default)
    {
        long sequence;
        lock (_lock)
        {
            sequence = _requestSequence.GetValueOrDefault(place.Key) + 1;
            _requestSequence[place.Key] = sequence;
            _lastAttempts[place.Key] = _time.GetUtcNow();
        }

        Forecast forecast;
        try
        {
            forecast = await _provider.FetchAsync(place, cancellationToken);
        }
        #pragma warning disable S2139 // Logged here with the place name and rethrown so the caller can show the failure.
        catch (Exception exception)
        #pragma warning restore S2139
        {
            _logger?.Warning(exception, "Forecast for {Place} could not be fetched", place.Name);
            throw;
        }

        forecast = forecast with { FetchedAt = _time.GetUtcNow() };
        var landed = false;
        lock (_lock)
        {
            // A place removed while the request ran is not saved again, and an older request never overwrites a newer one.
            if (_requestSequence.GetValueOrDefault(place.Key) == sequence && _places.Any(saved => saved.Key == place.Key))
            {
                _forecasts[place.Key] = forecast;
                landed = true;
            }
        }

        if (landed)
        {
            SaveForecasts();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // A download superseded or removed while it ran is not kept: the caller gets what the cache holds, or the download if there is none.
        return landed ? forecast : (GetForecast(place) ?? forecast);
    }

    public Task<IReadOnlyList<PlaceHit>> SearchAsync(string query, CancellationToken cancellationToken = default) =>
        _provider.SearchAsync(query, cancellationToken);

    public bool AddPlace(SavedPlace place)
    {
        lock (_lock)
        {
            if (_places.Any(saved => saved.Key == place.Key))
            {
                return false;
            }

            _places.Add(place);
        }

        SavePlaces();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool RemovePlace(SavedPlace place)
    {
        lock (_lock)
        {
            if (_places.RemoveAll(saved => saved.Key == place.Key) == 0)
            {
                return false;
            }

            _forecasts.Remove(place.Key);
            _lastAttempts.Remove(place.Key);
            if (_homeKey == place.Key)
            {
                _homeKey = null;
            }
        }

        SavePlaces();
        SaveHome();
        SaveForecasts();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SetHome(SavedPlace place)
    {
        lock (_lock)
        {
            if (!_places.Any(saved => saved.Key == place.Key))
            {
                return;
            }

            _homeKey = place.Key;
        }

        SaveHome();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private T? Load<T>(string key) where T : class
    {
        var text = _settings.Get(key);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException exception)
        {
            // A damaged value is cleared, so the next save writes a sound one. The places and forecasts come back as they are fetched.
            _logger?.Warning(exception, "Weather setting {Key} could not be read", key);
            _settings.Set(key, string.Empty);
            return null;
        }
    }

    private void Save<T>(string key, T value) => _settings.Set(key, JsonSerializer.Serialize(value, Json));

    /// <summary>The places are copied under the lock and written outside it, in order (see <see cref="SaveForecasts"/>).</summary>
    private void SavePlaces()
    {
        lock (_saveLock)
        {
            List<SavedPlace> snapshot;
            lock (_lock)
            {
                snapshot = [.. _places];
            }

            Save(PlacesKey, snapshot);
        }
    }

    /// <summary>The home place's key is copied under the lock and written outside it, in order.</summary>
    private void SaveHome()
    {
        lock (_saveLock)
        {
            string home;
            lock (_lock)
            {
                home = _homeKey ?? string.Empty;
            }

            _settings.Set(HomeKey, home);
        }
    }

    /// <summary>
    /// Writes the forecasts. The copy is taken under the lock, but the serialising is not, so the dialog's drawing is never held up
    /// by it. The save lock comes first, so a later copy is always written after an earlier one.
    /// </summary>
    private void SaveForecasts()
    {
        lock (_saveLock)
        {
            Dictionary<string, Forecast> snapshot;
            lock (_lock)
            {
                snapshot = new Dictionary<string, Forecast>(_forecasts);
            }

            Save(ForecastsKey, snapshot);
        }
    }
}
