using System.Text;
using iConsole.Services;

namespace iConsole.Test.Services;

/// <summary>Reading an iCalendar file: events, tasks and alarms, with their dates shown as they read.</summary>
[TestClass]
public class ICalendarTextReaderTest
{
    private static IReadOnlyList<string> Read(string text) => ICalendarTextReader.Read(Encoding.UTF8.GetBytes(text));

    [DataTestMethod]
    [DataRow("DTSTART:20260301", "  Start: 2026-03-01 (all day)")]
    [DataRow("DTSTART;VALUE=DATE:20260301", "  Start: 2026-03-01 (all day)")]
    [DataRow("DTSTART:20260301T090000Z", "  Start: 2026-03-01 09:00 UTC")]
    [DataRow("DTSTART;TZID=Europe/Budapest:20260301T090000", "  Start: 2026-03-01 09:00 Europe/Budapest")]
    [DataRow("DTSTART:20260301T090000", "  Start: 2026-03-01 09:00")]
    public void Dates_AreShownAsTheyRead(string property, string expected)
    {
        IReadOnlyList<string> lines = Read($"BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:X\n{property}\nEND:VEVENT\nEND:VCALENDAR\n");

        CollectionAssert.Contains(lines.ToArray(), expected);
    }

    [TestMethod]
    public void AnEvent_ShowsItsSummaryDatesAndOrganizerWithoutMailto()
    {
        const string calendar = """
            BEGIN:VCALENDAR
            VERSION:2.0
            X-WR-CALNAME:Work
            BEGIN:VEVENT
            UID:1@example
            DTSTAMP:20260301T080000Z
            DTSTART:20260305T090000Z
            DTEND;TZID=Europe/Budapest:20260305T100000
            SUMMARY:Planning\, round two
            LOCATION:Room 4
            ORGANIZER;CN=Jane Doe:mailto:jane@example.com
            END:VEVENT
            END:VCALENDAR
            """;

        CollectionAssert.AreEqual(
            new[]
            {
                "Calendar: Work",
                "--------------",
                "",
                "Event: Planning, round two",
                "  Start: 2026-03-05 09:00 UTC",
                "  End: 2026-03-05 10:00 Europe/Budapest",
                "  Location: Room 4",
                "  Organizer: Jane Doe <jane@example.com>",
            },
            Read(calendar).ToArray());
    }

    [TestMethod]
    public void AnAlarm_IsNestedUnderItsEvent()
    {
        IReadOnlyList<string> lines = Read(
            "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Standup\nBEGIN:VALARM\nTRIGGER:-PT15M\nACTION:DISPLAY\nEND:VALARM\nEND:VEVENT\nEND:VCALENDAR\n");

        CollectionAssert.Contains(lines.ToArray(), "  Alarm");
        CollectionAssert.Contains(lines.ToArray(), "    Trigger: -PT15M");
        CollectionAssert.Contains(lines.ToArray(), "    Action: DISPLAY");
    }

    [TestMethod]
    public void AMultiLineDescription_ContinuesOnIndentedLines()
    {
        IReadOnlyList<string> lines = Read(
            "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Review\nDESCRIPTION:Agenda:\\n1. Budget\\n2. Hiring\nEND:VEVENT\nEND:VCALENDAR\n");

        CollectionAssert.Contains(lines.ToArray(), "  Description: Agenda:");
        CollectionAssert.Contains(lines.ToArray(), "    1. Budget");
        CollectionAssert.Contains(lines.ToArray(), "    2. Hiring");
    }

    [TestMethod]
    public void AllDayEventAndTask_AreHeadedByTheirKind()
    {
        IReadOnlyList<string> lines = Read(
            "BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:Holiday\nDTSTART;VALUE=DATE:20260310\nEND:VEVENT\n"
            + "BEGIN:VTODO\nSUMMARY:Send the report\nDUE:20260401T120000\nEND:VTODO\nEND:VCALENDAR\n");

        CollectionAssert.Contains(lines.ToArray(), "Event: Holiday");
        CollectionAssert.Contains(lines.ToArray(), "  Start: 2026-03-10 (all day)");
        CollectionAssert.Contains(lines.ToArray(), "Task: Send the report");
        CollectionAssert.Contains(lines.ToArray(), "  Due: 2026-04-01 12:00");
    }

    [TestMethod]
    public void AFileWithNoCalendarItems_SaysSo()
    {
        CollectionAssert.AreEqual(new[] { "[No calendar items were found in this file]" }, Read("hello\n").ToArray());
    }
}
