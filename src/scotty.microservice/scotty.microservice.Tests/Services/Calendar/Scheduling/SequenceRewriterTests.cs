using weesky.Scotty.Microservice.Services.Calendar.Invitations;
using weesky.Scotty.Microservice.Services.Calendar.Scheduling;
using weesky.Scotty.Microservice.Tests.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Scheduling;

public class SequenceRewriterTests
{
    [Fact]
    public void Bump_AdvancesTheNamedComponentsOnly_AndWritesTheLineWhenAbsent()
    {
        var ics = InvitationParserTests.Fixture("webmail-invited-override");

        var master = SequenceRewriter.Bump(ics, At(""));
        Assert.Equal(2, InvitationParser.SequenceOf(master));
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20261019T100000\r\nDTSTAMP:20260913T100000Z\r\nSEQUENCE:0", master);

        var both = SequenceRewriter.Bump(ics, At("", "20261019T100000"));
        Assert.Contains("\r\nSEQUENCE:1\r\nSUMMARY:Réunion de rentrée\r\nLOCATION:Salle 2\r\nDTSTART;TZID=Europe/Brussels:20261019T150000", both);

        var without = ics.Replace("SEQUENCE:1\r\n", "").Replace("SEQUENCE:0\r\n", "");
        var written = SequenceRewriter.Bump(without, At(""));
        Assert.Equal(1, InvitationParser.SequenceOf(written));
        Assert.Equal(ics.Length - "SEQUENCE:0\r\n".Length, written.Length);
    }

    [Fact]
    public void Bump_LeavesEveryOtherByte()
    {
        var ics = InvitationParserTests.Fixture("webmail-invited");
        var bumped = SequenceRewriter.Bump(ics, At(""));
        Assert.Equal(ics.Replace("SEQUENCE:0", "SEQUENCE:1"), bumped);
        Assert.Equal(ics, SequenceRewriter.Bump(ics, At()));
    }

    // A quoted TZID parameter may hold a ':' of its own (a Windows zone name, "(UTC+01:00)
    // Amsterdam") — the value's ':' is the first one outside quotes, not the first one on the line.
    [Fact]
    public void Bump_ReadsTheRecurrenceIdKey_PastAColonInsideAQuotedParameter()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:u1\r\n" +
            "RECURRENCE-ID;TZID=\"(UTC+01:00) Amsterdam\":20261019T100000\r\nSEQUENCE:0\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var bumped = SequenceRewriter.Bump(ics, At("20261019T100000"));

        Assert.Contains("SEQUENCE:1", bumped);
    }

    // RFC 9074 lets a VALARM carry its own UID (Apple writes one); inserting the missing SEQUENCE
    // after every UID line would duplicate it there instead of placing it once, on the VEVENT.
    [Fact]
    public void Bump_WhenSequenceIsMissing_InsertsOnceAfterTheEventsOwnUid_NeverANestedAlarmsUid()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:u1\r\nDTSTART:20261019T100000Z\r\n" +
            "BEGIN:VALARM\r\nACTION:DISPLAY\r\nUID:alarm-1\r\nTRIGGER:-PT15M\r\nEND:VALARM\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var bumped = SequenceRewriter.Bump(ics, At(""));

        Assert.Single(bumped.Split("\r\n"), l => l == "SEQUENCE:1");
        Assert.Contains("UID:u1\r\nSEQUENCE:1\r\nDTSTART", bumped);
    }

    // "SEQUENCE;X-FOO=BAR:2" is still the SEQUENCE property, and the parameter is not the server's
    // to drop: only the value past the last ':' changes.
    [Fact]
    public void Bump_FindsAParameterizedSequenceLine_AndKeepsItsParameters()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:u1\r\n" +
            "SEQUENCE;X-FOO=BAR:2\r\nDTSTART:20261019T100000Z\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var bumped = SequenceRewriter.Bump(ics, At(""));

        Assert.Single(bumped.Split("\r\n"), l => l.StartsWith("SEQUENCE", StringComparison.Ordinal));
        Assert.Contains("SEQUENCE;X-FOO=BAR:3", bumped);
    }

    [Fact]
    public void Bump_ParsesASequenceBeyondIntRange()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:u1\r\nSEQUENCE:9999999999\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var bumped = SequenceRewriter.Bump(ics, At(""));

        Assert.Contains("SEQUENCE:10000000000", bumped);
    }

    [Fact]
    public void Bump_LeavesAComponentUntouched_WhenItsSequenceValueIsNotNumeric()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:u1\r\nSEQUENCE:abc\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        var bumped = SequenceRewriter.Bump(ics, At(""));

        Assert.Equal(ics, bumped);
    }
    // The floor of a stale copy: what the invitees hold, not the copy's own number, is what the
    // rewrite passes — on a SEQUENCE line that went down, and on a component that lost its line.
    [Fact]
    public void Bump_WritesAboveTheReference_WhenTheFileFellBelowIt()
    {
        const string ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:u1\r\nSEQUENCE:1\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

        Assert.Contains("\r\nSEQUENCE:6\r\n", SequenceRewriter.Bump(ics, new Dictionary<string, int> { [""] = 5 }));
        Assert.Contains("\r\nSEQUENCE:2\r\n", SequenceRewriter.Bump(ics, new Dictionary<string, int> { [""] = 1 }));
        Assert.Contains("UID:u1\r\nSEQUENCE:4\r\n", SequenceRewriter.Bump(ics.Replace("SEQUENCE:1\r\n", ""), new Dictionary<string, int> { [""] = 3 }));
    }

    private static IReadOnlyDictionary<string, int> At(params string[] keys) =>
        keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
}
