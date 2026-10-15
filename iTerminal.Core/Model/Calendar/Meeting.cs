using System.Globalization;
using System.Text.Json;
using iTerminal.Core.Services;

namespace iTerminal.Core.Model.Calendar;

/// <summary>
/// A meeting on one day, from <see cref="Start"/> for <see cref="DurationMinutes"/>. It ends the same
/// day: a meeting that would run past midnight is refused by <see cref="TryParse"/>, so a day's list
/// never needs to know about the next one.
/// </summary>
public sealed record Meeting(DateOnly Date, TimeOnly Start, int DurationMinutes, string Title)
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
            error = "Type a start time and a title, e.g. 14:30 Standup";
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

    private readonly List<Meeting> _meetings = [];
    private bool _loaded;

    /// <summary>The day's meetings, earliest first.</summary>
    public IReadOnlyList<Meeting> On(DateOnly date)
    {
        EnsureLoaded();
        return _meetings.Where(m => m.Date == date).OrderBy(m => m.Start).ThenBy(m => m.Title, StringComparer.Ordinal).ToList();
    }

    public void Add(Meeting meeting)
    {
        EnsureLoaded();
        _meetings.Add(meeting);
        Save();
    }

    public void Remove(Meeting meeting)
    {
        EnsureLoaded();
        if (_meetings.Remove(meeting))
        {
            Save();
        }
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
            if (!string.IsNullOrWhiteSpace(json))
            {
                _meetings.AddRange(JsonSerializer.Deserialize<List<Meeting>>(json, Json) ?? []);
            }
        }
        catch (JsonException)
        {
            // A damaged value must not stop the calendar opening; the next save replaces it.
        }
    }

    private void Save()
    {
        settingsService.Set(SettingsKey, JsonSerializer.Serialize(_meetings, Json));
        settingsService.Write();
    }
}
