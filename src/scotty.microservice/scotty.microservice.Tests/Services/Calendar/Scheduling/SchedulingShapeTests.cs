using weesky.Scotty.Microservice.Services.Calendar.Scheduling;
using weesky.Scotty.Microservice.Tests.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Scheduling;

public class SchedulingShapeTests
{
    private static string Fixture(string name) => InvitationParserTests.Fixture(name);

    [Fact]
    public void Of_ChangesWhenOnlyAnOverrideMoves_WhileTheMasterDoesNot()
    {
        var series = Fixture("webmail-invited");
        var moved = Fixture("webmail-invited-override");

        Assert.NotEqual(SchedulingShape.Of(series), SchedulingShape.Of(moved));
        // The master's own line is identical in both: the difference is the override alone.
        Assert.StartsWith(SchedulingShape.Of(series)!.Split('\n')[0], SchedulingShape.Of(moved)!);
    }

    [Fact]
    public void Of_IgnoresAPartStat_AndTheCaseOfAnAddress()
    {
        var before = Fixture("webmail-invited");
        var answered = before.Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=DECLINED")
            .Replace("mailto:Julie@Example.net", "mailto:JULIE@EXAMPLE.NET");

        Assert.Equal(SchedulingShape.Of(before), SchedulingShape.Of(answered));
        Assert.Equal(SchedulingShape.HashOf(SchedulingShape.Of(before)!), SchedulingShape.HashOf(SchedulingShape.Of(answered)!));
    }

    [Fact]
    public void Of_ChangesOnTitle_Location_AndGuestList_ButNotOnDescription()
    {
        var before = Fixture("webmail-invited");
        var shape = SchedulingShape.Of(before);

        Assert.NotEqual(shape, SchedulingShape.Of(before.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion")));
        Assert.NotEqual(shape, SchedulingShape.Of(before.Replace("LOCATION:Salle 2", "LOCATION:Salle 3")));
        Assert.NotEqual(shape, SchedulingShape.Of(before.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "")));
        Assert.Equal(shape, SchedulingShape.Of(before.Replace("LOCATION:Salle 2", "LOCATION:Salle 2\r\nDESCRIPTION:Apportez le dossier")));
    }

    [Fact]
    public void Of_WithoutAttendees_SeesTheSameEvent_WhateverTheGuests()
    {
        var before = Fixture("webmail-invited");
        var fewer = before.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "");

        Assert.Equal(SchedulingShape.Of(before, withAttendees: false), SchedulingShape.Of(fewer, withAttendees: false));
        Assert.Null(SchedulingShape.Of("not a calendar"));
    }

    [Fact]
    public void Addresses_AreEveryAttendee_Lowercased_WithoutMailto_AndTheOrganizerIsRead()
    {
        var addresses = SchedulingShape.Addresses(Fixture("webmail-invited-override"));

        Assert.Equal(new HashSet<string> { "marc.dupont@example.org", "julie@example.net" }, addresses);
        Assert.Equal("alice@weesky.be", SchedulingShape.OrganizerOf(Fixture("webmail-invited")));
        Assert.Null(SchedulingShape.OrganizerOf(Fixture("webmail-invited").Replace("ORGANIZER;CN=Alice:mailto:alice@weesky.be\r\n", "")));
    }

    // A client that moved a component without advancing its SEQUENCE: the master here (a new
    // title, SEQUENCE still 0), named with the SEQUENCE it had before. The second fixture's new
    // override at SEQUENCE:0 is no lag: the master it came with advanced 0 → 1, the client versioned the change.
    [Fact]
    public void ChangedWithoutBump_NamesTheComponentsWhoseShapeMoved_WithoutASequenceStep()
    {
        var before = Fixture("webmail-invited");
        var retitled = before.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion");
        var retitledAndBumped = retitled.Replace("SEQUENCE:0", "SEQUENCE:1");

        Assert.Equal(References(("", 0)), SchedulingShape.ChangedWithoutBump(before, retitled));
        Assert.Empty(SchedulingShape.ChangedWithoutBump(before, retitledAndBumped));
        Assert.Empty(SchedulingShape.ChangedWithoutBump(before, Fixture("webmail-invited-override")));

        var overrideMovedAgain = Fixture("webmail-invited-override").Replace("DTSTART;TZID=Europe/Brussels:20261019T150000", "DTSTART;TZID=Europe/Brussels:20261019T160000");
        Assert.Equal(References(("20261019T100000", 0)), SchedulingShape.ChangedWithoutBump(Fixture("webmail-invited-override"), overrideMovedAgain));
    }

    // Spec § 9: an override moved alone advances alone. New in the file, it lags when its SEQUENCE
    // does not pass what the invitees hold of the master, and the master did not advance either.
    [Fact]
    public void ChangedWithoutBump_NamesANewOverride_AtOrBelowTheMastersSequence_WhenTheMasterDidNotAdvance()
    {
        var before = Fixture("webmail-invited").Replace("SEQUENCE:0", "SEQUENCE:2");
        var withOverride = Fixture("webmail-invited-override").Replace("SEQUENCE:1", "SEQUENCE:2");

        Assert.Equal(References(("20261019T100000", 2)), SchedulingShape.ChangedWithoutBump(before, withOverride));
        Assert.Equal(References(("20261019T100000", 2)), SchedulingShape.ChangedWithoutBump(before, withOverride.Replace("SEQUENCE:0", "SEQUENCE:2")));
        Assert.Empty(SchedulingShape.ChangedWithoutBump(before, withOverride.Replace("SEQUENCE:0", "SEQUENCE:3")));
        Assert.Empty(SchedulingShape.ChangedWithoutBump(before, withOverride.Replace("SEQUENCE:2", "SEQUENCE:3")));
    }

    // A stale device copy: its SEQUENCE went down while its shape moved. The reference is what the
    // invitees hold, so the rewrite ends above it, never at the copy's own number plus one.
    [Fact]
    public void ChangedWithoutBump_ReferencesTheSequenceBefore_WhenACopysSequenceWentDown()
    {
        var before = Fixture("webmail-invited").Replace("SEQUENCE:0", "SEQUENCE:5");
        var stale = Fixture("webmail-invited").Replace("SEQUENCE:0", "SEQUENCE:3").Replace("LOCATION:Salle 2", "LOCATION:Salle 3");

        Assert.Equal(References(("", 5)), SchedulingShape.ChangedWithoutBump(before, stale));
    }

    private static IReadOnlyDictionary<string, int> References(params (string Key, int Sequence)[] entries) =>
        entries.ToDictionary(e => e.Key, e => e.Sequence, StringComparer.Ordinal);

    // Naively joined with "|", a title carrying the separator can borrow from the next field:
    // SUMMARY "a|b" + no LOCATION joins to the same text as SUMMARY "a" + LOCATION "b|".
    [Fact]
    public void Of_DoesNotCollide_WhenATitlesPipeCouldBorrowFromTheLocationField()
    {
        var pipeInTitle = Calendar(summary: "a|b", location: null);
        var pipeInLocation = Calendar(summary: "a", location: "b|");

        Assert.NotEqual(SchedulingShape.Of(pipeInTitle), SchedulingShape.Of(pipeInLocation));
    }

    private static string Calendar(string summary, string? location)
    {
        var locationLine = location is null ? "" : $"LOCATION:{location}\r\n";
        return "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:u1\r\nDTSTART:20261019T100000Z\r\n" +
            $"SUMMARY:{summary}\r\n" + locationLine + "END:VEVENT\r\nEND:VCALENDAR\r\n";
    }
}
