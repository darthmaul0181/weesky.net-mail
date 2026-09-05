using System.Globalization;

namespace weesky.Snoopy.Microservice.Tests.Fixtures;

/// <summary>
/// Minimal VCALENDAR bodies written by hand, CRLF included, for the tests that need one exact
/// shape rather than a real client's file. The corpus in <c>Fixtures/ICalendar</c> is what proves
/// the engine reads the field; these prove it reads one rule.
/// </summary>
internal static class Ics
{
    internal const string Zone = "Europe/Brussels";

    private const string Head = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//tests//EN\r\n";
    private const string Tail = "END:VCALENDAR\r\n";
    private const string Stamp = "DTSTAMP:20260901T080000Z\r\n";
    private const string InstantFormat = "yyyyMMdd'T'HHmmss";

    internal static string Events(params (string Uid, string? RecurrenceId)[] components)
    {
        var text = Head;
        foreach (var (uid, recurrenceId) in components)
        {
            var day = recurrenceId ?? "20260907";
            text += "BEGIN:VEVENT\r\nUID:" + uid + "\r\n" + Stamp
                    + (recurrenceId is null ? "" : "RECURRENCE-ID;VALUE=DATE:" + recurrenceId + "\r\n")
                    + "DTSTART;VALUE=DATE:" + day + "\r\nSUMMARY:Day\r\nEND:VEVENT\r\n";
        }

        return text + Tail;
    }

    internal static string Todo() =>
        Head + "BEGIN:VTODO\r\nUID:todo\r\n" + Stamp + "SUMMARY:Buy milk\r\nEND:VTODO\r\n" + Tail;

    internal static string Rule(string rrule, string? extra = null) =>
        Head + "BEGIN:VEVENT\r\nUID:rule\r\n" + Stamp
        + "DTSTART;TZID=" + Zone + ":20260907T090000\r\nDTEND;TZID=" + Zone + ":20260907T100000\r\n"
        + "RRULE:" + rrule + "\r\n" + Line(extra) + "END:VEVENT\r\n" + Tail;

    /// <summary>The same event as <see cref="Rule"/> with DTSTART in UTC: no zone means no hour a
    /// wall clock reads twice, so a sub-daily rule stays admissible and walkable there.</summary>
    internal static string RuleInUtc(string rrule, string? extra = null) =>
        Head + "BEGIN:VEVENT\r\nUID:rule\r\n" + Stamp
        + "DTSTART:20260907T090000Z\r\nDTEND:20260907T100000Z\r\n"
        + "RRULE:" + rrule + "\r\n" + Line(extra) + "END:VEVENT\r\n" + Tail;

    internal static string WeeklyWithoutZone() => Rule("FREQ=WEEKLY");

    internal static string RuleWithOverride(string rrule, string overrideStart, string? extra = null, string? summary = null)
    {
        var start = DateTime.ParseExact(overrideStart, InstantFormat, CultureInfo.InvariantCulture);
        return Head + "BEGIN:VEVENT\r\nUID:rule\r\n" + Stamp
               + "DTSTART;TZID=" + Zone + ":20260907T090000\r\nDTEND;TZID=" + Zone + ":20260907T100000\r\n"
               + "RRULE:" + rrule + "\r\n" + Line(extra) + Line(Summary(summary)) + "END:VEVENT\r\n"
               + "BEGIN:VEVENT\r\nUID:rule\r\n" + Stamp
               + "RECURRENCE-ID;TZID=" + Zone + ":20260914T090000\r\n"
               + "DTSTART;TZID=" + Zone + ":" + overrideStart + "\r\n"
               + "DTEND;TZID=" + Zone + ":" + start.AddHours(1).ToString(InstantFormat, CultureInfo.InvariantCulture) + "\r\n"
               + Line(Summary(summary is null ? null : summary + " (moved)")) + "END:VEVENT\r\n" + Tail;
    }

    /// <summary>Le 7, le 14 déplacé à 11:00, le 21 retiré par EXDATE, le 28.</summary>
    internal static string WeeklyWithExdateAndOverride() =>
        RuleWithOverride("FREQ=WEEKLY", "20260914T110000",
            extra: "EXDATE;TZID=" + Zone + ":20260921T090000", summary: "Standup");

    /// <summary>Weekly all-day event from Monday 7 September: DATE values everywhere.</summary>
    internal static string AllDayWeekly() =>
        Head + "BEGIN:VEVENT\r\nUID:allday\r\n" + Stamp
        + "DTSTART;VALUE=DATE:20260907\r\nDTEND;VALUE=DATE:20260908\r\nRRULE:FREQ=WEEKLY\r\nSUMMARY:Chores\r\nEND:VEVENT\r\n" + Tail;

    /// <summary>
    /// What a phone leaves on the server: a VTIMEZONE, X-APPLE- lines, STATUS:CONFIRMED and an
    /// explicit TRANSP:OPAQUE, SEQUENCE:2, a DISPLAY reminder carrying its X-WR-ALARMUID, an EMAIL
    /// alarm relative to the end, and the 14th moved to 11:00. Everything a rewrite must leave alone.
    /// </summary>
    internal static string FromPhone() =>
        Head + SeasonalZone(Zone)
        + "BEGIN:VEVENT\r\nUID:phone\r\n" + Stamp
        + "CREATED:20260830T120000Z\r\nLAST-MODIFIED:20260901T080000Z\r\nSEQUENCE:2\r\n"
        + "DTSTART;TZID=" + Zone + ":20260907T090000\r\nDTEND;TZID=" + Zone + ":20260907T100000\r\n"
        + "RRULE:FREQ=WEEKLY\r\nSUMMARY:Standup\r\nLOCATION:Room 4\r\nDESCRIPTION:Daily sync\r\n"
        + "STATUS:CONFIRMED\r\nTRANSP:OPAQUE\r\nX-APPLE-TRAVEL-ADVISORY-BEHAVIOR:AUTOMATIC\r\n"
        + "BEGIN:VALARM\r\nX-WR-ALARMUID:0A1B2C3D\r\nUID:0A1B2C3D\r\nACTION:DISPLAY\r\nTRIGGER:-PT15M\r\n"
        + "DESCRIPTION:Standup\r\nEND:VALARM\r\n"
        + "BEGIN:VALARM\r\nACTION:EMAIL\r\nTRIGGER;RELATED=END:-PT5M\r\nSUMMARY:Standup\r\nDESCRIPTION:Over\r\n"
        + "ATTENDEE:mailto:michel@weesky.be\r\nEND:VALARM\r\n"
        + "END:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:phone\r\n" + Stamp
        + "RECURRENCE-ID;TZID=" + Zone + ":20260914T090000\r\n"
        + "DTSTART;TZID=" + Zone + ":20260914T110000\r\nDTEND;TZID=" + Zone + ":20260914T120000\r\n"
        + "SUMMARY:Standup (moved)\r\nEND:VEVENT\r\n" + Tail;

    internal static string WithAttendees() =>
        Head + "BEGIN:VEVENT\r\nUID:meeting\r\n" + Stamp
        + "DTSTART;TZID=" + Zone + ":20260907T090000\r\nDTEND;TZID=" + Zone + ":20260907T100000\r\n"
        + "RRULE:FREQ=WEEKLY;COUNT=3\r\nORGANIZER;CN=Michel:mailto:michel@weesky.be\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:meeting\r\n" + Stamp
        + "RECURRENCE-ID;TZID=" + Zone + ":20260914T090000\r\n"
        + "DTSTART;TZID=" + Zone + ":20260914T110000\r\nDTEND;TZID=" + Zone + ":20260914T120000\r\n"
        + "ATTENDEE;CN=Lea;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:lea@example.org\r\nEND:VEVENT\r\n"
        + Tail;

    internal static string Single(string start, string? end, string? extra = null, string? zone = null) =>
        Head + (zone ?? "") + "BEGIN:VEVENT\r\nUID:single\r\n" + Stamp
        + start + "\r\n" + Line(end) + Line(extra) + "END:VEVENT\r\n" + Tail;

    /// <summary>A well-formed resource whose DESCRIPTION pads it to exactly <paramref name="bytes"/>.</summary>
    internal static string Padded(int bytes)
    {
        var head = Head + "BEGIN:VEVENT\r\nUID:padded\r\n" + Stamp + "DTSTART:20260907T090000Z\r\nDESCRIPTION:";
        var tail = "\r\nEND:VEVENT\r\n" + Tail;
        return head + new string('x', Math.Max(0, bytes - head.Length - tail.Length)) + tail;
    }

    /// <summary>A VTIMEZONE whose id is a Windows one — the mapping decides, not this block.</summary>
    internal static string WindowsZone(string tzid) => FixedZone(tzid, "+0100");

    internal static string FixedZone(string tzid, string offset) =>
        "BEGIN:VTIMEZONE\r\nTZID:" + tzid + "\r\nBEGIN:STANDARD\r\nDTSTART:19700101T000000\r\n"
        + "TZOFFSETFROM:" + offset + "\r\nTZOFFSETTO:" + offset + "\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\n";

    /// <summary>A VTIMEZONE with two observances, so which one answers changes the offset: +0200
    /// from the last Sunday of March, +0100 from the last Sunday of October.</summary>
    internal static string SeasonalZone(string tzid) =>
        "BEGIN:VTIMEZONE\r\nTZID:" + tzid + "\r\n"
        + "BEGIN:STANDARD\r\nDTSTART:20001029T030000\r\nRRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU\r\n"
        + "TZOFFSETFROM:+0200\r\nTZOFFSETTO:+0100\r\nEND:STANDARD\r\n"
        + "BEGIN:DAYLIGHT\r\nDTSTART:20000326T020000\r\nRRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU\r\n"
        + "TZOFFSETFROM:+0100\r\nTZOFFSETTO:+0200\r\nEND:DAYLIGHT\r\nEND:VTIMEZONE\r\n";

    /// <summary>A VTIMEZONE whose observance carries a rule an expander may refuse: BYSETPOS with
    /// nothing to select from.</summary>
    internal static string BrokenZone(string tzid) =>
        "BEGIN:VTIMEZONE\r\nTZID:" + tzid + "\r\nBEGIN:STANDARD\r\nDTSTART:19700101T000000\r\n"
        + "RRULE:FREQ=YEARLY;BYSETPOS=1\r\nTZOFFSETFROM:+0300\r\nTZOFFSETTO:+0300\r\n"
        + "END:STANDARD\r\nEND:VTIMEZONE\r\n";

    /// <summary>
    /// What a calendar export really looks like: an envelope nothing but the file owns, one weekly
    /// series carrying two overrides, an all-day event, a VTODO the collection does not store, and
    /// a VEVENT with no DTSTART at all — the one component of the five the store must refuse.
    /// </summary>
    internal static string GoogleLikeExport() =>
        "BEGIN:VCALENDAR\r\nPRODID:-//Google Inc//Google Calendar 70.9054//EN\r\nVERSION:2.0\r\n"
        + "CALSCALE:GREGORIAN\r\nMETHOD:PUBLISH\r\nX-WR-CALNAME:michel@weesky.be\r\n"
        + "X-WR-TIMEZONE:" + Zone + "\r\n" + SeasonalZone(Zone)
        + "BEGIN:VEVENT\r\nUID:standup@google.com\r\n" + Stamp
        + "DTSTART;TZID=" + Zone + ":20260907T090000\r\nDTEND;TZID=" + Zone + ":20260907T093000\r\n"
        + "RRULE:FREQ=WEEKLY\r\nSUMMARY:Standup\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:standup@google.com\r\n" + Stamp
        + "RECURRENCE-ID;TZID=" + Zone + ":20260914T090000\r\n"
        + "DTSTART;TZID=" + Zone + ":20260914T110000\r\nDTEND;TZID=" + Zone + ":20260914T113000\r\n"
        + "SUMMARY:Standup (moved)\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:standup@google.com\r\n" + Stamp
        + "RECURRENCE-ID;TZID=" + Zone + ":20260921T090000\r\n"
        + "DTSTART;TZID=" + Zone + ":20260921T100000\r\nDTEND;TZID=" + Zone + ":20260921T103000\r\n"
        + "SUMMARY:Standup (later)\r\nEND:VEVENT\r\n"
        + "BEGIN:VEVENT\r\nUID:lunch@google.com\r\n" + Stamp
        + "DTSTART;VALUE=DATE:20260910\r\nDTEND;VALUE=DATE:20260911\r\nSUMMARY:Lunch\r\nEND:VEVENT\r\n"
        + "BEGIN:VTODO\r\nUID:todo@google.com\r\n" + Stamp + "SUMMARY:Buy milk\r\nEND:VTODO\r\n"
        + "BEGIN:VEVENT\r\nUID:broken@google.com\r\n" + Stamp + "SUMMARY:No start at all\r\nEND:VEVENT\r\n"
        + Tail;

    /// <summary>A resource carrying no UID of its own — what the store's one text surgery is for.</summary>
    internal static string EventWithoutUid() =>
        Head + "BEGIN:VEVENT\r\n" + Stamp
        + "DTSTART:20260907T090000Z\r\nDTEND:20260907T100000Z\r\nSUMMARY:Anonymous\r\nEND:VEVENT\r\n" + Tail;

    /// <summary>A rule the PUT gate refuses on density: one instance a minute, for ever.</summary>
    internal static string DensityBomb() =>
        Head + "BEGIN:VEVENT\r\nUID:bomb\r\n" + Stamp
        + "DTSTART:20260907T090000Z\r\nDTEND:20260907T090100Z\r\nRRULE:FREQ=MINUTELY\r\n"
        + "SUMMARY:Tick\r\nEND:VEVENT\r\n" + Tail;

    private static string Line(string? value) => value is null ? "" : value + "\r\n";

    private static string? Summary(string? value) => value is null ? null : "SUMMARY:" + value;
}
