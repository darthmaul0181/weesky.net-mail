using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationParserTests
{
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Invitations", name + ".ics"));

    [Theory]
    [InlineData("google-request", "Dîner chez Marc", "marc.dupont@example.org", 3, 0)]
    [InlineData("outlook-request", "Point équipe", "rh@example.com", 1, 2)]
    [InlineData("apple-request", "Toussaint", "paul@example.org", 1, 0)]
    [InlineData("thunderbird-request", "Répétition", "lea@example.net", 2, 1)]
    public void ReadsARequestFromEveryBigClient(string name, string summary, string organizer, int attendees, int sequence)
    {
        var reading = InvitationParser.Read(Fixture(name));

        var invitation = Assert.IsType<ParsedInvitation>(reading.Invitation);
        Assert.False(reading.Ignored);
        Assert.Equal(InvitationMethod.Request, invitation.Method);
        Assert.Equal(summary, invitation.Summary);
        Assert.Equal(organizer, invitation.Organizer!.Email);
        Assert.Equal(attendees, invitation.Attendees.Count);
        Assert.Equal(sequence, invitation.Sequence);
        Assert.False(invitation.OccurrenceOnly);
    }

    [Fact]
    public void Google_PlacesTheZonedStartInUtc_AndKeepsTheDtStartLine()
    {
        var invitation = InvitationParser.Read(Fixture("google-request")).Invitation!;

        Assert.Equal(new DateTime(2026, 10, 10, 17, 30, 0, DateTimeKind.Utc), invitation.Start);
        Assert.Equal(new DateTime(2026, 10, 10, 20, 30, 0, DateTimeKind.Utc), invitation.End);
        // Assert.Equal(DateTime, DateTime) ignores Kind: without these two, an instant read as
        // Unspecified would pass and every consumer would re-zone it a second time.
        Assert.Equal(DateTimeKind.Utc, invitation.Start!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, invitation.End!.Value.Kind);
        Assert.False(invitation.IsAllDay);
        Assert.Equal("Rue des Lilas 12, 1000 Bruxelles", invitation.Location);
        Assert.Equal("DTSTART;TZID=Europe/Brussels:20261010T193000", invitation.DtStartLine);
        // The user's ATTENDEE keeps the file's casing here — bar the domain, which Uri lowercases
        // for every address the module reads; matching is the reader's job.
        Assert.Contains(invitation.Attendees, a => a.Email == "Alice@weesky.be" && a.PartStat == "NEEDS-ACTION");
        Assert.Equal("Marc Dupont", invitation.Organizer!.Name);
    }

    [Fact]
    public void Outlook_Repeats_AndUnfoldsTheAttendeeAddress()
    {
        var invitation = InvitationParser.Read(Fixture("outlook-request")).Invitation!;

        Assert.True(invitation.Repeats);
        Assert.Equal("ALICE@weesky.be", invitation.Attendees[0].Email);
        Assert.Equal("ALICE", invitation.Attendees[0].Name);
        Assert.Equal("Service RH", invitation.Organizer!.Name);
    }

    [Fact]
    public void Apple_IsAWholeDay_WithDatesAndNoInstants()
    {
        var invitation = InvitationParser.Read(Fixture("apple-request")).Invitation!;

        Assert.True(invitation.IsAllDay);
        Assert.Null(invitation.Start);
        Assert.Equal(new DateOnly(2026, 11, 1), invitation.StartDate);
        Assert.Equal(new DateOnly(2026, 11, 2), invitation.EndDateExclusive);
        Assert.Null(invitation.Organizer!.Name);
    }

    [Fact]
    public void Cancel_IsRead_WithItsSequence()
    {
        var invitation = InvitationParser.Read(Fixture("google-cancel")).Invitation!;

        Assert.Equal(InvitationMethod.Cancel, invitation.Method);
        Assert.Equal(1, invitation.Sequence);
    }

    [Fact]
    public void Reply_IsIgnored_NotUnreadable()
    {
        var reading = InvitationParser.Read(Fixture("google-reply"));

        Assert.Null(reading.Invitation);
        Assert.True(reading.Ignored);
        Assert.Null(reading.Reason);
    }

    [Fact]
    public void NoMethod_IsIgnored()
    {
        var reading = InvitationParser.Read(Fixture("google-request").Replace("METHOD:REQUEST\r\n", ""));

        Assert.True(reading.Ignored);
    }

    [Fact]
    public void AnOccurrenceAlone_IsOccurrenceOnly()
    {
        var invitation = InvitationParser.Read(Fixture("occurrence-cancel")).Invitation!;

        Assert.True(invitation.OccurrenceOnly);
        Assert.Equal(3, invitation.Sequence);
        Assert.Equal(new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), invitation.Start);
    }

    /// <summary>The DTSTART line the REPLY pairs on must be the master's, whatever order the
    /// components are written in — the file's first one belongs to the moved date.</summary>
    [Fact]
    public void AnOverrideWrittenBeforeItsMaster_StillDescribesTheMaster()
    {
        var invitation = InvitationParser.Read(Fixture("google-override-first")).Invitation!;

        Assert.False(invitation.OccurrenceOnly);
        Assert.True(invitation.Repeats);
        Assert.Equal("DTSTART;TZID=Europe/Brussels:20261010T193000", invitation.DtStartLine);
        Assert.Equal(new DateTime(2026, 10, 10, 17, 30, 0, DateTimeKind.Utc), invitation.Start);
    }

    [Fact]
    public void AFileTheGuardsRefuse_IsUnreadable_WithTheReason()
    {
        // No DTSTART: IcsGuards.CheckStart refuses what every stored file must carry.
        var reading = InvitationParser.Read(Fixture("thunderbird-request").Replace("DTSTART:20261015T170000Z\r\n", ""));

        Assert.Null(reading.Invitation);
        Assert.False(reading.Ignored);
        Assert.NotNull(reading.Reason);
    }

    [Fact]
    public void Garbage_IsUnreadable()
    {
        var reading = InvitationParser.Read("not a calendar");

        Assert.False(reading.Ignored);
        Assert.NotNull(reading.Reason);
    }

    [Fact]
    public void SequenceOf_ReadsTheMaster_AndZeroWhenAbsentOrUnreadable()
    {
        Assert.Equal(2, InvitationParser.SequenceOf(Fixture("outlook-request")));
        Assert.Equal(0, InvitationParser.SequenceOf(Fixture("apple-request")));
        Assert.Equal(0, InvitationParser.SequenceOf("garbage"));
    }

    [Fact]
    public void PartStatOf_MatchesTheAddressWithoutCase()
    {
        Assert.Equal("NEEDS-ACTION", InvitationParser.PartStatOf(Fixture("outlook-request"), "alice@weesky.be"));
        Assert.Null(InvitationParser.PartStatOf(Fixture("outlook-request"), "nobody@weesky.be"));
    }
}
