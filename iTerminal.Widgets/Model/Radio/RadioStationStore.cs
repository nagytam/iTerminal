using System.Text.Json;
using iTerminal.Core.Services;

namespace iTerminal.Widgets.Model.Radio;

/// <summary>
/// The internet radio stations the person keeps, as one JSON list in the settings table under <see cref="SettingsKey"/>,
/// and the last few stations played, as a JSON list of ids under <see cref="RecentSettingsKey"/>.
/// Kept out of the database for the same reason as the contacts: a new table would change the schema stamp and clear
/// every cached table at start-up.
/// </summary>
public sealed class RadioStationStore(ISettingsService settingsService)
{
    public const string SettingsKey = "RadioStations";
    public const string RecentSettingsKey = "RadioRecent";

    /// <summary>How many stations the recently played list keeps.</summary>
    public const int RecentLimit = 5;

    private static readonly JsonSerializerOptions Json = new();

    private readonly List<RadioStation> _stations = [];

    /// <summary>The ids of the stations last started, most recent first, at most <see cref="RecentLimit"/>.</summary>
    private readonly List<Guid> _recent = [];
    private bool _loaded;

    /// <summary>
    /// The stations matching <paramref name="filter"/> (all of them when it is empty), favourites first and then by name.
    /// With <paramref name="favouritesOnly"/> the list is cut to the favourites.
    /// </summary>
    public IReadOnlyList<RadioStation> Find(string? filter = null, bool favouritesOnly = false)
    {
        EnsureLoaded();
        return _stations
            .Where(s => !favouritesOnly || s.IsFavourite)
            .Where(s => s.Matches(filter ?? string.Empty))
            .OrderByDescending(s => s.IsFavourite)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int Count
    {
        get
        {
            EnsureLoaded();
            return _stations.Count;
        }
    }

    /// <summary>The station with this id, or null when it has been removed.</summary>
    public RadioStation? Get(Guid id)
    {
        EnsureLoaded();
        return _stations.FirstOrDefault(s => s.Id == id);
    }

    /// <summary>True when another station already plays from <paramref name="url"/>; a station is never listed twice.</summary>
    public bool HasUrl(string url, Guid? exceptId = null)
    {
        EnsureLoaded();
        return _stations.Any(s => s.Id != exceptId && string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(RadioStation station)
    {
        EnsureLoaded();
        _stations.Add(station);
        Save();
    }

    /// <summary>Replaces the station with the same <see cref="RadioStation.Id"/>.</summary>
    public void Update(RadioStation station)
    {
        EnsureLoaded();
        var index = _stations.FindIndex(s => s.Id == station.Id);
        if (index < 0)
        {
            return;
        }

        _stations[index] = station;
        Save();
    }

    public void Remove(Guid id)
    {
        EnsureLoaded();
        if (_stations.RemoveAll(s => s.Id == id) > 0)
        {
            _recent.Remove(id);
            Save();
        }
    }

    /// <summary>The last stations started, most recent first, at most <see cref="RecentLimit"/>. A station since removed is skipped.</summary>
    public IReadOnlyList<RadioStation> Recent()
    {
        EnsureLoaded();
        return _recent
            .Select(id => _stations.FirstOrDefault(s => s.Id == id))
            .OfType<RadioStation>()
            .ToList();
    }

    /// <summary>Moves the station to the top of the recently played list, dropping the oldest once the list is full.</summary>
    public void RecordPlayed(Guid id)
    {
        EnsureLoaded();
        if (_stations.All(s => s.Id != id))
        {
            return;
        }

        _recent.Remove(id);
        _recent.Insert(0, id);
        if (_recent.Count > RecentLimit)
        {
            _recent.RemoveRange(RecentLimit, _recent.Count - RecentLimit);
        }

        Save();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            var json = settingsService.Get(SettingsKey);
            if (json is null)
            {
                // Never saved: start with the preseeded stations. Loading does not save (the dialog reads the list while
                // drawing); the first change writes the whole list, and a list the person empties stays empty.
                _stations.AddRange(DefaultRadioStations.All);
            }
            else if (!string.IsNullOrWhiteSpace(json))
            {
                // A null entry is not a station, and an older entry may lack a text field; the rest is still good.
                _stations.AddRange((JsonSerializer.Deserialize<List<RadioStation>>(json, Json) ?? [])
                    .Where(station => station is not null)
                    .Select(s => s with { Name = s.Name ?? string.Empty, Url = s.Url ?? string.Empty, Genre = s.Genre ?? string.Empty }));
            }
        }
        catch (JsonException)
        {
            // A damaged value must not stop the dialog opening; the next save replaces it.
        }

        try
        {
            var json = settingsService.Get(RecentSettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _recent.AddRange((JsonSerializer.Deserialize<List<Guid>>(json, Json) ?? [])
                    .Distinct()
                    .Take(RecentLimit));
            }
        }
        catch (JsonException)
        {
            // The recent list is only a convenience; a damaged one starts empty and the next play replaces it.
        }
    }

    private void Save()
    {
        settingsService.Set(SettingsKey, JsonSerializer.Serialize(_stations, Json));
        settingsService.Set(RecentSettingsKey, JsonSerializer.Serialize(_recent, Json));
        settingsService.Write();
    }
}
