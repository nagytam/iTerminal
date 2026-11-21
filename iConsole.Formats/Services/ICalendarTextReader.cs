using iConsole.Helper;

using System.Text.RegularExpressions;

namespace iConsole.Services;

/// <summary>
/// Reads an iCalendar (.ics) file into lines: the calendar's name, then each event, task, journal entry or alarm under
/// a heading with its summary and its properties in turn. Dates are shown as they read (2026-03-01 09:00 UTC rather than
/// 20260301T090000Z), attendees and organizers without their mailto: prefix, and nested alarms are indented under the
/// event they belong to. Recurrence rules are shown as written, and the time zone definitions are reduced to a name.
/// </summary>
public static partial class ICalendarTextReader
{
    /// <summary>A component (VEVENT, VTODO, VALARM …) and the properties and components it holds.</summary>
    private sealed class Component(string name)
    {
        public string Name { get; } = name;
        public List<ContentLine> Properties { get; } = [];
        public List<Component> Children { get; } = [];
    }

    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["VEVENT"] = "Event", ["VTODO"] = "Task", ["VJOURNAL"] = "Journal", ["VFREEBUSY"] = "Free/busy",
        ["VALARM"] = "Alarm", ["VTIMEZONE"] = "Time zone", ["VCALENDAR"] = "Calendar",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DTSTART"] = "Start", ["DTEND"] = "End", ["DUE"] = "Due", ["DURATION"] = "Duration", ["LOCATION"] = "Location",
        ["DESCRIPTION"] = "Description", ["ORGANIZER"] = "Organizer", ["ATTENDEE"] = "Attendee", ["STATUS"] = "Status",
        ["RRULE"] = "Repeats", ["EXDATE"] = "Except", ["RDATE"] = "Also on", ["RECURRENCE-ID"] = "Recurrence of",
        ["CATEGORIES"] = "Categories", ["URL"] = "URL", ["PRIORITY"] = "Priority", ["TRIGGER"] = "Trigger",
        ["ACTION"] = "Action", ["COMMENT"] = "Comment", ["CONTACT"] = "Contact", ["GEO"] = "Location (geo)",
        ["PERCENT-COMPLETE"] = "Complete", ["COMPLETED"] = "Completed", ["CLASS"] = "Class",
    };

    /// <summary>Properties that are bookkeeping (identifiers, stamps, file metadata), or that are already the heading.</summary>
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "SUMMARY", "UID", "DTSTAMP", "CREATED", "LAST-MODIFIED", "SEQUENCE", "TRANSP", "PRODID", "VERSION", "CALSCALE",
        "METHOD", "X-WR-CALNAME", "X-WR-TIMEZONE", "X-WR-CALDESC", "TZID", "TZOFFSETFROM", "TZOFFSETTO", "TZNAME",
    };

    /// <summary>Properties whose value is a date, or a list of them.</summary>
    private static readonly HashSet<string> DateProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "DTSTART", "DTEND", "DUE", "EXDATE", "RDATE", "RECURRENCE-ID", "COMPLETED",
    };

    [GeneratedRegex(@"^(?<year>\d{4})(?<month>\d{2})(?<day>\d{2})(?:T(?<hour>\d{2})(?<minute>\d{2})(?:\d{2})?(?<utc>Z)?)?$")]
    private static partial Regex DateValue();

    public static IReadOnlyList<string> Read(byte[] content)
    {
        List<string> lines = [];
        try
        {
            Component root = Parse(TextEncodings.Decode(content, out _));
            List<Component> calendars = root.Children.Where(c => c.Name.Equals("VCALENDAR", StringComparison.OrdinalIgnoreCase)).ToList();
            IEnumerable<Component> sources = calendars.Count > 0 ? calendars : root.Children;
            foreach (Component calendar in sources)
            {
                AppendCalendar(lines, calendar);
            }

            if (lines.Count == 0)
            {
                lines.Add("[No calendar items were found in this file]");
            }
        }
        catch (Exception exception)
        {
            lines.Add($"[Could not read the calendar: {exception.Message}]");
        }

        return lines;
    }

    /// <summary>Builds the component tree: BEGIN opens a component, END closes it, and every other line is a property of the open one.</summary>
    private static Component Parse(string text)
    {
        Component root = new("ROOT");
        Stack<Component> open = new();
        open.Push(root);
        foreach (string logical in ContentLines.Unfold(text))
        {
            ContentLine? parsed = ContentLines.Parse(logical);
            if (parsed is not { } line)
            {
                continue;
            }

            if (line.Name.Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                Component child = new(line.Value.Trim().ToUpperInvariant());
                open.Peek().Children.Add(child);
                open.Push(child);
            }
            else if (line.Name.Equals("END", StringComparison.OrdinalIgnoreCase))
            {
                if (open.Count > 1)
                {
                    open.Pop();
                }
            }
            else
            {
                open.Peek().Properties.Add(line);
            }
        }

        return root;
    }

    private static void AppendCalendar(List<string> lines, Component calendar)
    {
        string? calendarName = Value(calendar, "X-WR-CALNAME");
        lines.Add(string.IsNullOrWhiteSpace(calendarName) ? "Calendar" : $"Calendar: {calendarName}");
        lines.Add(new string('-', Math.Max(3, lines[^1].Length)));
        foreach (Component component in calendar.Children)
        {
            lines.Add(string.Empty);
            if (component.Name.Equals("VTIMEZONE", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"Time zone: {Value(component, "TZID") ?? "(unnamed)"}");
                continue;
            }

            AppendComponent(lines, component, 0);
        }
    }

    private static void AppendComponent(List<string> lines, Component component, int depth)
    {
        string indent = new(' ', depth * 2);
        string kind = Kinds.TryGetValue(component.Name, out string? name) ? name : component.Name;
        string? summary = Value(component, "SUMMARY");
        // An alarm has no summary of its own, so it is headed by its kind alone.
        lines.Add(summary is null && kind == "Alarm" ? $"{indent}Alarm" : $"{indent}{kind}: {summary ?? "(untitled)"}");

        foreach (ContentLine property in component.Properties)
        {
            if (Hidden.Contains(property.Name))
            {
                continue;
            }

            string value = Describe(property);
            if (value.Length > 0)
            {
                ContentLines.AppendValue(lines, $"{indent}  {Label(property.Name)}: ", $"{indent}    ", value);
            }
        }

        foreach (Component child in component.Children)
        {
            AppendComponent(lines, child, depth + 1);
        }
    }

    private static string Describe(ContentLine property)
    {
        string name = property.Name.ToUpperInvariant();
        if (DateProperties.Contains(name))
        {
            string tzid = property.Parameter("TZID") ?? string.Empty;
            bool dateOnly = string.Equals(property.Parameter("VALUE"), "DATE", StringComparison.OrdinalIgnoreCase);
            return string.Join(", ", property.Value.Split(',').Select(v => FormatDate(v.Trim(), tzid, dateOnly)));
        }

        if (name is "ORGANIZER" or "ATTENDEE")
        {
            string address = property.Value;
            if (address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                address = address["mailto:".Length..];
            }

            string? common = property.Parameter("CN");
            return string.IsNullOrWhiteSpace(common) ? address : $"{common} <{address}>";
        }

        if (name == "CATEGORIES")
        {
            return string.Join(", ", ContentLines.SplitStructured(property.Value, ','));
        }

        return ContentLines.Unescape(property.Value);
    }

    /// <summary>
    /// Shows an iCalendar date: 20260301 as 2026-03-01 (all day), 20260301T090000Z as 2026-03-01 09:00 UTC, and a local
    /// time with its time zone name when it has one. Anything else is shown as it was written.
    /// </summary>
    internal static string FormatDate(string value, string timeZone, bool dateOnly)
    {
        Match match = DateValue().Match(value);
        if (!match.Success)
        {
            return value;
        }

        string date = $"{match.Groups["year"].Value}-{match.Groups["month"].Value}-{match.Groups["day"].Value}";
        if (!match.Groups["hour"].Success || dateOnly)
        {
            return $"{date} (all day)";
        }

        string time = $"{match.Groups["hour"].Value}:{match.Groups["minute"].Value}";
        string zone = match.Groups["utc"].Success ? " UTC" : timeZone.Length > 0 ? $" {timeZone}" : string.Empty;
        return $"{date} {time}{zone}";
    }

    private static string? Value(Component component, string name)
    {
        foreach (ContentLine property in component.Properties)
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return ContentLines.Unescape(property.Value);
            }
        }

        return null;
    }

    private static string Label(string name) =>
        Labels.TryGetValue(name, out string? label) ? label
        : name.StartsWith("X-", StringComparison.OrdinalIgnoreCase) ? name[2..]
        : name;
}
