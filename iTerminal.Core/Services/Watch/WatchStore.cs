using System.Text.Json;
using iTerminal.Core.Model.Watch;

namespace iTerminal.Core.Services.Watch;

/// <summary>
/// The alarms and the world-clock zones the person keeps, each as a JSON list in the settings table. Not a database
/// table: the app has no migrations, and a new table would change the schema stamp and drop every cached table at the
/// next start-up. Like <see cref="Model.Calendar.MeetingStore"/>, the list is read on first use and written whole.
/// </summary>
public sealed class WatchStore(ISettingsService settingsService)
{
    public const string AlarmsKey = "WatchAlarms";
    public const string WorldClocksKey = "WatchWorldClocks";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // Guards the lists. The dialog (UI thread) and WatchAlarmJob (its own thread) both use them.
    private readonly Lock _gate = new();

    // Serialises the settings writes, so the list left in the settings is always the newest snapshot. Taken before _gate, never the other way round.
    private readonly Lock _writeGate = new();
    private List<WatchAlarm> _alarms = [];
    private List<string> _worldClocks = [];
    private bool _loaded;

    public IReadOnlyList<WatchAlarm> Alarms
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                #pragma warning disable S2365 // Callers read a snapshot taken under the lock; a property would copy on every read.
                return _alarms.ToList();
                #pragma warning restore S2365
            }
        }
    }

    public IReadOnlyList<string> WorldClocks
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                #pragma warning disable S2365 // Callers read a snapshot taken under the lock; a property would copy on every read.
                return _worldClocks.ToList();
                #pragma warning restore S2365
            }
        }
    }

    public WatchAlarm AddAlarm(TimeOnly time, string label)
    {
        WatchAlarm alarm;
        lock (_gate)
        {
            EnsureLoaded();
            alarm = new WatchAlarm(Guid.NewGuid().ToString("N"), time, label);
            _alarms.Add(alarm);
        }

        Persist(AlarmsKey, () => JsonSerializer.Serialize(_alarms, Json));
        return alarm;
    }

    /// <summary>Replaces the alarm with the same <see cref="WatchAlarm.Uid"/>. An alarm that is not stored changes nothing.</summary>
    public void ReplaceAlarm(WatchAlarm alarm)
    {
        lock (_gate)
        {
            EnsureLoaded();
            int index = _alarms.FindIndex(a => a.Uid == alarm.Uid);
            if (index < 0)
            {
                return;
            }

            _alarms[index] = alarm;
        }

        Persist(AlarmsKey, () => JsonSerializer.Serialize(_alarms, Json));
    }

    public void RemoveAlarm(string uid)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_alarms.RemoveAll(a => a.Uid == uid) == 0)
            {
                return;
            }
        }

        Persist(AlarmsKey, () => JsonSerializer.Serialize(_alarms, Json));
    }

    /// <summary>Records that the alarm rang on <paramref name="day"/>, so it does not ring again that day.</summary>
    public void MarkRung(string uid, DateOnly day)
    {
        lock (_gate)
        {
            EnsureLoaded();
            int index = _alarms.FindIndex(a => a.Uid == uid);
            if (index < 0)
            {
                return;
            }

            _alarms[index] = _alarms[index] with { LastRungOn = day };
        }

        Persist(AlarmsKey, () => JsonSerializer.Serialize(_alarms, Json));
    }

    /// <summary>Adds a zone by its system id. A zone already listed, or one the computer does not know, adds nothing and returns false.</summary>
    public bool AddWorldClock(string id)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (!WatchTimeZones.TryFind(id, out var zone) || _worldClocks.Contains(zone.Id, StringComparer.Ordinal))
            {
                return false;
            }

            _worldClocks.Add(zone.Id);
        }

        Persist(WorldClocksKey, () => JsonSerializer.Serialize(_worldClocks, Json));
        return true;
    }

    public void RemoveWorldClock(string id)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_worldClocks.RemoveAll(zone => zone == id) == 0)
            {
                return;
            }
        }

        Persist(WorldClocksKey, () => JsonSerializer.Serialize(_worldClocks, Json));
    }

    /// <summary>
    /// Reads both lists once. A damaged value is treated as an empty list, so the dialog still opens, and the next save
    /// replaces it. Called under <see cref="_gate"/>.
    /// </summary>
    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _alarms = ReadList<WatchAlarm>(AlarmsKey).Where(alarm => alarm is not null).ToList();
        _worldClocks = ReadList<string>(WorldClocksKey).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
    }

    private List<T> ReadList<T>(string key)
    {
        try
        {
            var json = settingsService.Get(key);
            return string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<T>>(json, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Writes one list. The snapshot is taken by <paramref name="serialize"/> under the write gate, and the lock is taken inside it, so a write never races another.</summary>
    private void Persist(string key, Func<string> serialize)
    {
        lock (_writeGate)
        {
            string json;
            lock (_gate)
            {
                json = serialize();
            }

            settingsService.Set(key, json);
        }
    }
}
