using System.Text;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class ItipCalendarTests
{
    private const string OutlookStart =
        "DTSTART;TZID=\"(UTC+01:00) Amsterdam, Berlin, Bern, Rome, Stockholm, Vienna\":20261010T193000";

    private static readonly DateTime Now = new(2026, 9, 13, 8, 0, 0, DateTimeKind.Utc);

    private static string Cancel(string summary) => ItipCalendar.Reduced(
        method: "CANCEL", uid: "uid-1", sequence: 2, nowUtc: Now, organizerEmail: "alice@weesky.be", organizerName: "Alice",
        attendees: [("marc.dupont@example.org", null, null)], dtStartLine: OutlookStart, summary: summary, status: "CANCELLED");

    [Fact]
    public void LongLines_AreFoldedAt75Octets_NeverInsideACharacter_AndReadBackWhole()
    {
        var summary = string.Concat(Enumerable.Repeat("R\u00e9union de rentr\u00e9e; ", 6)).TrimEnd();

        var ics = Cancel(summary);

        var physical = ics.Split("\r\n");
        Assert.Equal(string.Empty, physical[^1]);
        Assert.All(physical[..^1], line => Assert.InRange(Encoding.UTF8.GetByteCount(line), 1, 75));
        Assert.Contains(physical, line => line.StartsWith(' '));
        var logical = PartStatRewriter.Unfold(ics).Select(l => l.Text).ToList();
        Assert.Contains(OutlookStart, logical);
        Assert.Contains("SUMMARY:" + ItipCalendar.EscapeText(summary), logical);
        Assert.Equal(summary, IcsDocument.TryLoad(ics)!.Events.Single().Summary);
    }

    // A textual builder never writes an address a mail could not be sent to: `mailto:a b@…` is no URI.
    [Theory]
    [InlineData("a b@example.org", "alice@weesky.be")]
    [InlineData("marc@example.org", "a>b@example.org")]
    public void RefusesToWriteAnUndeliverableAddress(string organizer, string attendee) =>
        Assert.Throws<ArgumentException>(() => ItipCalendar.Reduced(
            method: "REPLY", uid: "uid-1", sequence: 0, nowUtc: Now, organizerEmail: organizer, organizerName: null,
            attendees: [(attendee, null, "ACCEPTED")], dtStartLine: null, summary: null));

    // Ical.Net unescapes a UID's "\n": written as it reads, it would splice a line of its own into the file.
    [Theory]
    [InlineData("uid-1\nX-EVIL:1")]
    [InlineData("uid-1\r\nX-EVIL:1")]
    [InlineData("uid-1\u0000")]
    public void RefusesAUidHoldingAControlCharacter(string uid) =>
        Assert.Throws<ArgumentException>(() => ItipCalendar.Reduced(
            method: "REPLY", uid: uid, sequence: 0, nowUtc: Now, organizerEmail: "marc@example.org", organizerName: null,
            attendees: [("alice@weesky.be", null, "ACCEPTED")], dtStartLine: null, summary: null));

    [Fact]
    public void TheSkeleton_InItsOrder_WithAttendeeParameters()
    {
        var ics = ItipCalendar.Reduced(
            method: "REPLY", uid: "uid-1", sequence: 0, nowUtc: Now, organizerEmail: "marc@example.org", organizerName: null,
            attendees: [("alice@weesky.be", "Martin, Alice", "ACCEPTED")], dtStartLine: null, summary: null);

        Assert.Equal(
            "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//weesky//webmail//EN\r\nMETHOD:REPLY\r\nBEGIN:VEVENT\r\nUID:uid-1\r\n"
            + "SEQUENCE:0\r\nDTSTAMP:20260913T080000Z\r\nORGANIZER:mailto:marc@example.org\r\n"
            + "ATTENDEE;PARTSTAT=ACCEPTED;CN=\"Martin, Alice\":mailto:alice@weesky.be\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n", ics);
    }
}
