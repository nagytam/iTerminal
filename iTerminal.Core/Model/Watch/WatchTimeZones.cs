using System.Globalization;
using iConsole.Localization;

namespace iTerminal.Core.Model.Watch;

/// <summary>
/// The time zones the world clock shows. A zone is named by its system id (<c>Europe/Budapest</c>), which is what the
/// person types and what is stored; the computer's own zone is always listed first and is not stored.
/// </summary>
public static class WatchTimeZones
{
    /// <summary>Finds the zone with <paramref name="id"/>. The lookup is by system id and is case-sensitive on Linux.</summary>
    public static bool TryFind(string id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>The wall-clock time in <paramref name="zone"/> at the UTC instant <paramref name="utcNow"/>.</summary>
    public static DateTime ConvertFromUtc(DateTime utcNow, TimeZoneInfo zone)
    {
        var utc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }

    /// <summary>The label of the computer's own zone in the list.</summary>
    public static string LocalLabel => UiText.Get("Watch.ThisComputer", "This computer");

    /// <summary>The offset of <paramref name="zone"/> from UTC at <paramref name="utcNow"/>, as <c>UTC+02:00</c>.</summary>
    public static string OffsetLabel(DateTime utcNow, TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"UTC{sign}{offset.Duration():hh\\:mm}";
    }

    /// <summary>Formats a wall-clock time as <c>HH:mm:ss</c>, invariant so the digits stay the same in every language.</summary>
    public static string FormatTime(DateTime time) => time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
