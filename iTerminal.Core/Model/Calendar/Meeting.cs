using iConsole.Localization;
using System.Globalization;
using System.Text.Json;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model.Calendar;

/// <summary>
/// A meeting on one day, from <see cref="Start"/> for <see cref="DurationMinutes"/>. It ends the same
/// day: a meeting that would run past midnight is refused by <see cref="TryParse"/>, so a day's list
/// never needs to know about the next one.
/// <see cref="Uid"/> is the meeting's identity: it is given when the store adds the meeting, keeps it through
/// an edit, and survives a restart. A meeting that has not been stored yet has none.
/// </summary>
public sealed record Meeting(DateOnly Date, TimeOnly Start, int DurationMinutes, string Title, string? Uid = null)
{
    public const int DefaultDurationMinutes = 60;

    public TimeOnly End => Start.AddMinutes(DurationMinutes);

    /// <summary>"14:30–15:15  Standup", the line the dialog lists.</summary>
    public string Describe() => $"{Start:HH\\:mm}–{End:HH\\:mm}  {Title}";

    /// <summary>
    /// Reads what the person typed for a day: a start time, an optional duration in minutes, then the
    /// title. <c>14:30 Standup</c> is an hour from half past two; <c>14:30 45 Standup</c> is 45 minutes.
    /// </summary>
    public static bool TryParse(string text, DateOnly date, out Meeting? meeting, out string error)
    {
        meeting = null;
        var tokens = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            error = UiText.Get("Meeting.TypeTime", "Type a start time and a title, e.g. 14:30 Standup");
            return false;
        }

        if (!TimeOnly.TryParseExact(tokens[0], ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
        {
            error = $"\"{tokens[0]}\" is not a start time; use HH:mm, e.g. 09:30";
            return false;
        }

        var duration = DefaultDurationMinutes;
        var titleTokens = tokens[1..];
        if (tokens.Length >= 3 && int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
        {
            duration = minutes;
            titleTokens = tokens[2..];
        }

        var title = string.Join(' ', titleTokens);
        if (title.Length == 0)
        {
            error = "A meeting needs a title after its time";
            return false;
        }

        if (start.ToTimeSpan().TotalMinutes + duration > 24 * 60)
        {
            error = "A meeting has to end on the same day";
            return false;
        }

        meeting = new Meeting(date, start, duration, title);
        error = string.Empty;
        return true;
    }
}

/// <summary>
/// The meetings the person has added, kept as one JSON list in the settings table under
/// <see cref="SettingsKey"/>. Not a database table: the app has no migrations, and a new table would
/// change the schema stamp and throw away every cached table at the next start-up.
/// </summary>
public sealed class MeetingStore(ISettingsService settingsService)
{
    public const string SettingsKey = "CalendarMeetings";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    // Guards the list and the pending flag. The dialog (UI thread) and CalendarIdentityJob (its own thread) both use it.
    private readonly Lock _gate = new();

    // Serialises the settings writes (see Persist). Taken before _gate, never the other way round.
    private readonly Lock _writeGate = new();
    private readonly List<Meeting> _meetings = [];
    private bool _loaded;

    /// <summary>
    /// True while some loaded meeting has an identity that is not yet in the settings. It is set when a meeting
    /// without an identity is loaded (a list saved before identities existed) and cleared once a save succeeds.
    /// </summary>
    private bool _identitiesPending;

    /// <summary>The day's meetings, earliest first.</summary>
    public IReadOnlyList<Meeting> On(DateOnly date)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _meetings.Where(m => m.Date == date).OrderBy(m => m.Start).ThenBy(m => m.Title, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Stores <paramref name="meeting"/> under a new identity and returns the stored copy, which carries it.</summary>
    public Meeting Add(Meeting meeting)
    {
        Meeting stored;
        lock (_gate)
        {
            EnsureLoaded();
            stored = Identified(meeting);
            _meetings.Add(stored);
        }

        Persist();
        return stored;
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> in the place of <paramref name="existing"/> and keeps its identity, so an
    /// edit is one save and the event stays the same event. An <paramref name="existing"/> that is not stored is added.
    /// Returns the stored copy.
    /// </summary>
    public Meeting Replace(Meeting existing, Meeting replacement)
    {
        Meeting stored;
        lock (_gate)
        {
            EnsureLoaded();
            var index = existing.Uid is null ? -1 : _meetings.FindIndex(m => m.Uid == existing.Uid);
            if (index < 0)
            {
                stored = Identified(replacement);
                _meetings.Add(stored);
            }
            else
            {
                stored = replacement with { Uid = existing.Uid };
                _meetings[index] = stored;
            }
        }

        Persist();
        return stored;
    }

    /// <summary>Removes the stored meeting with the same identity. A meeting without one (never stored) removes nothing.</summary>
    public void Remove(Meeting meeting)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (meeting.Uid is null || _meetings.RemoveAll(m => m.Uid == meeting.Uid) == 0)
            {
                return;
            }
        }

        Persist();
    }

    /// <summary>True while identities given at load time are not yet in the settings (see <see cref="PersistPendingIdentities"/>).</summary>
    public bool HasPendingIdentities
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _identitiesPending;
            }
        }
    }

    /// <summary>
    /// Writes the list when identities were given at load time, so they are the same after a restart. Does nothing
    /// otherwise. A failed write throws and leaves the flag set, so the next call tries again.
    /// </summary>
    public void PersistPendingIdentities()
    {
        bool pending;
        lock (_gate)
        {
            EnsureLoaded();
            pending = _identitiesPending;
        }

        if (pending)
        {
            Persist();
        }
    }

    private static string NewUid() => Guid.NewGuid().ToString("N");

    private static Meeting Identified(Meeting meeting) => meeting.Uid is null ? meeting with { Uid = NewUid() } : meeting;

    /// <summary>
    /// Reads the list and gives every entry without an identity one in memory. It does not write: the identities are
    /// written by <see cref="PersistPendingIdentities"/>, off the render thread. Called under <see cref="_gate"/>.
    /// </summary>
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
            if (!string.IsNullOrWhiteSpace(json))
            {
                // A null entry ("[null]") is not a meeting; the rest of the list is still good.
                IEnumerable<Meeting> loaded = (JsonSerializer.Deserialize<List<Meeting>>(json, Json) ?? []).Where(meeting => meeting is not null);
                foreach (var meeting in loaded)
                {
                    if (meeting.Uid is null)
                    {
                        _meetings.Add(meeting with { Uid = NewUid() });
                        _identitiesPending = true;
                    }
                    else
                    {
                        _meetings.Add(meeting);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A damaged value must not stop the calendar opening; the next save replaces it.
        }
    }

    /// <summary>
    /// Writes the whole list. The snapshot is taken under <see cref="_gate"/>, but the settings write is not, so
    /// drawing (which reads the list under that lock) never waits for the disk. <see cref="_writeGate"/> keeps two
    /// writes in order, so the list left in the settings is always the newest snapshot. A failed write sets the
    /// pending flag again, so the identities are tried once more.
    /// </summary>
    private void Persist()
    {
        lock (_writeGate)
        {
            string json;
            bool wasPending;
            lock (_gate)
            {
                json = JsonSerializer.Serialize(_meetings, Json);
                wasPending = _identitiesPending;
                _identitiesPending = false;
            }

            try
            {
                settingsService.Set(SettingsKey, json);
                settingsService.Write();
            }
            catch
            {
                if (wasPending)
                {
                    lock (_gate)
                    {
                        _identitiesPending = true;
                    }
                }

                throw;
            }
        }
    }
}
