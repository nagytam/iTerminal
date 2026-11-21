using System.Globalization;
using iConsole.Localization;

namespace iTerminal.Core.Model.Watch;

/// <summary>
/// An alarm that rings every day at <see cref="Time"/> while it is <see cref="Enabled"/>. <see cref="LastRungOn"/> is the
/// day it last rang, so it rings once a day even when the check runs more than once in the same minute. <see cref="Uid"/>
/// is the alarm's identity across edits and restarts.
/// </summary>
public sealed record WatchAlarm(string Uid, TimeOnly Time, string Label, bool Enabled = true, DateOnly? LastRungOn = null)
{
    /// <summary>The label to show, or a plain "Alarm" when none was typed.</summary>
    public string DisplayLabel => Label.Length == 0 ? UiText.Get("Watch.DefaultAlarmLabel", "Alarm") : Label;

    /// <summary>True when the alarm should ring at <paramref name="now"/>: it is enabled, the minute is its minute and it has not rung yet today.</summary>
    public static bool IsDue(WatchAlarm alarm, DateTime now)
    {
        if (!alarm.Enabled)
        {
            return false;
        }

        var current = TimeOnly.FromDateTime(now);
        return current.Hour == alarm.Time.Hour
            && current.Minute == alarm.Time.Minute
            && alarm.LastRungOn != DateOnly.FromDateTime(now);
    }

    /// <summary>
    /// Reads what the person typed: a time, then an optional label. <c>07:30 Wake up</c>; the label may be left out.
    /// </summary>
    public static bool TryParse(string text, out TimeOnly time, out string label, out string error)
    {
        time = default;
        label = string.Empty;
        var tokens = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = UiText.Get("Watch.TypeTime", "Type a time and an optional label, e.g. 07:30 Wake up");
            return false;
        }

        if (!TimeOnly.TryParseExact(tokens[0], ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
        {
            error = string.Format(CultureInfo.CurrentCulture, UiText.Get("Watch.BadTime", "\"{0}\" is not a time; use HH:mm, e.g. 07:30"), tokens[0]);
            return false;
        }

        label = string.Join(' ', tokens[1..]);
        error = string.Empty;
        return true;
    }
}
