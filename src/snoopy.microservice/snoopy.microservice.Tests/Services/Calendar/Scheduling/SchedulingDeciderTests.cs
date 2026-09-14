using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Scheduling;

public class SchedulingDeciderTests
{
    private static readonly IReadOnlySet<string> Alice = new HashSet<string> { "alice@weesky.be", "alice@weesky.net" };
    private static string Fixture(string name) => InvitationParserTests.Fixture(name);
    private static readonly string Invited = Fixture("webmail-invited");
    private static string Hash(string ics) => SchedulingShape.HashOf(SchedulingShape.Of(ics)!);

    private static SchedulingDecision Decide(string? before, string? after, string? owner, WriteOrigin origin = WriteOrigin.Webmail, IReadOnlySet<string>? own = null) =>
        SchedulingDecider.Decide(new SchedulingInput(
            new SchedulingBefore(before, owner, before is null || owner is null ? null : Hash(before)), after, origin, own ?? Alice));

    [Fact]
    public void FirstWriteWithGuests_FromTheWebmail_InvitesEveryone_AndTakesOwnership()
    {
        var decision = Decide(null, Invited, owner: null);

        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Invitation, mail.Kind);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"], mail.Recipients.Order());
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(Invited), decision.Hash);
    }

    [Fact]
    public void TakingOverAnEventThunderbirdInvited_SaysUpdate()
    {
        var after = Invited.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var decision = Decide(Invited, after, owner: null);

        Assert.Equal(MailKind.Update, Assert.Single(decision.Mails).Kind);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(after), decision.Hash);
    }

    // A takeover behaves as if the stored file were the last version sent: nothing a guest
    // cares about changed, so ownership is taken silently and nobody is mailed.
    [Fact]
    public void TakeoverWithOnlyADescriptionChange_SendsNothing_AndTakesOwnership()
    {
        var withDescription = Invited.Replace("END:VEVENT", "DESCRIPTION:Apporter les slides\r\nEND:VEVENT");
        var decision = Decide(Invited, withDescription, owner: null);

        Assert.Empty(decision.Mails);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(withDescription), decision.Hash);
    }

    [Fact]
    public void TakeoverMovingTheCalendar_SameFile_SendsNothing_AndTakesOwnership()
    {
        var decision = Decide(Invited, Invited, owner: null);

        Assert.Empty(decision.Mails);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(Invited), decision.Hash);
    }

    [Fact]
    public void TakeoverAddingAGuest_InvitesOnlyThem()
    {
        var added = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:paul@example.org\r\nEND:VEVENT");
        var decision = Decide(Invited, added, owner: null);

        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Invitation, mail.Kind);
        Assert.Equal(["paul@example.org"], mail.Recipients);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(added), decision.Hash);
    }

    [Fact]
    public void TakeoverRemovingAGuest_CancelsOnlyThem()
    {
        var removed = Invited.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "");
        var decision = Decide(Invited, removed, owner: null);

        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Cancellation, mail.Kind);
        Assert.Equal(["julie@example.net"], mail.Recipients);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(removed), decision.Hash);
    }

    [Fact]
    public void TakeoverAddingAGuestAndChangingTheLocation_InvitesTheNewGuest_AndUpdatesTheOthers()
    {
        var addedAndMoved = Invited
            .Replace("END:VEVENT", "ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:paul@example.org\r\nEND:VEVENT")
            .Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var decision = Decide(Invited, addedAndMoved, owner: null);

        Assert.Equal(2, decision.Mails.Count);
        Assert.Equal(["paul@example.org"], decision.Mails.Single(m => m.Kind == MailKind.Invitation).Recipients);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"],
            decision.Mails.Single(m => m.Kind == MailKind.Update).Recipients.Order());
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(addedAndMoved), decision.Hash);
    }

    [Fact]
    public void ADeviceWriting_AnEventNobodyOwns_SendsNothing()
    {
        var decision = Decide(null, Invited, owner: null, origin: WriteOrigin.Device);

        Assert.Empty(decision.Mails);
        Assert.Null(decision.Owner);
        Assert.Null(decision.Hash);
    }

    [Fact]
    public void NotTheOrganizer_OrNoGuests_SendsNothing()
    {
        var received = Invited.Replace("ORGANIZER;CN=Alice:mailto:alice@weesky.be", "ORGANIZER:mailto:marc.dupont@example.org");
        Assert.Empty(Decide(null, received, owner: null).Mails);
        Assert.Null(Decide(null, received, owner: null).Owner);

        var alone = Invited.Split("\r\n").Where(l => !l.StartsWith("ATTENDEE", StringComparison.Ordinal)).Aggregate((a, b) => a + "\r\n" + b);
        Assert.Empty(Decide(null, alone, owner: null).Mails);
    }

    [Fact]
    public void UnchangedShape_SendsNothing_AndKeepsTheColumns()
    {
        var answered = Invited.Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=ACCEPTED");
        var decision = Decide(Invited, answered, owner: "webmail", origin: WriteOrigin.Device);

        Assert.Empty(decision.Mails);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(Invited), decision.Hash);
    }

    [Fact]
    public void ChangedShape_SameGuests_UpdatesEveryone_FromEitherDoor()
    {
        var moved = Fixture("webmail-invited-override");
        foreach (var origin in new[] { WriteOrigin.Webmail, WriteOrigin.Device })
        {
            var decision = Decide(Invited, moved, owner: "webmail", origin: origin);
            var mail = Assert.Single(decision.Mails);
            Assert.Equal(MailKind.Update, mail.Kind);
            Assert.Equal(2, mail.Recipients.Count);
            Assert.Equal(Hash(moved), decision.Hash);
        }
    }

    [Fact]
    public void GuestsAdded_AreInvited_AndTheOthersUpdatedOnlyIfSomethingElseChanged()
    {
        var added = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:paul@example.org\r\nEND:VEVENT");
        var decision = Decide(Invited, added, owner: "webmail");
        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Invitation, mail.Kind);
        Assert.Equal(["paul@example.org"], mail.Recipients);

        var addedAndMoved = added.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var both = Decide(Invited, addedAndMoved, owner: "webmail").Mails;
        Assert.Equal(2, both.Count);
        Assert.Equal(["paul@example.org"], both.Single(m => m.Kind == MailKind.Invitation).Recipients);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"], both.Single(m => m.Kind == MailKind.Update).Recipients.Order());
    }

    [Fact]
    public void GuestsRemoved_AreCancelledAlone_AndTheRestUpdatedOnlyIfSomethingElseChanged()
    {
        var removed = Invited.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "");
        var decision = Decide(Invited, removed, owner: "webmail");
        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Cancellation, mail.Kind);
        Assert.Equal(["julie@example.net"], mail.Recipients);
        Assert.Equal("webmail", decision.Owner);

        var removedAndMoved = removed.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion");
        var both = Decide(Invited, removedAndMoved, owner: "webmail").Mails;
        Assert.Equal(["marc.dupont@example.org"], both.Single(m => m.Kind == MailKind.Update).Recipients);
    }

    [Fact]
    public void NoGuestLeft_OrDeleted_CancelsEveryone_AndReleasesOwnership()
    {
        var alone = Invited.Split("\r\n").Where(l => !l.StartsWith("ATTENDEE", StringComparison.Ordinal)).Aggregate((a, b) => a + "\r\n" + b);
        foreach (var after in new[] { alone, null })
        {
            var decision = Decide(Invited, after, owner: "webmail", origin: WriteOrigin.Device);
            var mail = Assert.Single(decision.Mails);
            Assert.Equal(MailKind.Cancellation, mail.Kind);
            Assert.Equal(["julie@example.net", "marc.dupont@example.org"], mail.Recipients.Order());
            Assert.Null(decision.Owner);
            Assert.Null(decision.Hash);
        }
    }

    [Fact]
    public void DeletingAnEventNobodyOwns_SendsNothing()
    {
        Assert.Empty(Decide(Invited, null, owner: null).Mails);
    }

    // The organizer listed among the guests — Google's and Apple's habit, and any of the user's
    // own addresses — never receives their own invitation.
    [Fact]
    public void TheUsersOwnAddresses_AreNeverRecipients()
    {
        var withSelf = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=ACCEPTED:mailto:Alice@weesky.net\r\nEND:VEVENT");
        var decision = Decide(null, withSelf, owner: null);

        Assert.DoesNotContain("alice@weesky.net", Assert.Single(decision.Mails).Recipients);
    }

    // Julie leaves the 19 Oct override alone; the master still invites her, so the file-level
    // guest set does not move — yet that date's hash did, and something has to be sent for it.
    [Fact]
    public void GuestRemovedFromOneOverrideOnly_KeepsTheGuestList_ButStillSendsAnUpdate()
    {
        const string julieLine = "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n";
        var overrideIcs = Fixture("webmail-invited-override");
        var first = overrideIcs.IndexOf(julieLine, StringComparison.Ordinal);
        var second = overrideIcs.IndexOf(julieLine, first + julieLine.Length, StringComparison.Ordinal);
        var after = overrideIcs.Remove(second, julieLine.Length);

        var decision = Decide(overrideIcs, after, owner: "webmail");

        var mail = Assert.Single(decision.Mails);
        Assert.Equal(MailKind.Update, mail.Kind);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"], mail.Recipients.Order());
    }

    // The shape counts every attendee, the user's own address included; Guests strips it back out.
    // Adding, removing or duplicating only the user's own line must change the hash without mailing.
    [Fact]
    public void OwnAttendeeLineAdded_SendsNothing_ButUpdatesTheHash()
    {
        var withSelf = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=ACCEPTED:mailto:alice@weesky.be\r\nEND:VEVENT");
        var decision = Decide(Invited, withSelf, owner: "webmail", origin: WriteOrigin.Device);

        Assert.Empty(decision.Mails);
        Assert.Equal("webmail", decision.Owner);
        Assert.Equal(Hash(withSelf), decision.Hash);
    }

    [Fact]
    public void DuplicateAttendeeLineForAnExistingGuest_SendsNothing()
    {
        var duplicated = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=ACCEPTED:mailto:marc.dupont@example.org\r\nEND:VEVENT");

        Assert.Empty(Decide(Invited, duplicated, owner: "webmail").Mails);
    }

    // before.Ics is the last STORED file, not necessarily the last one invitees were told about
    // (an import, or a failed hook, can land in between): a stale before.Hash must still update
    // the unaffected guests, on top of inviting whoever was newly added.
    [Fact]
    public void DriftedBeforeHash_WithAnAddedGuest_InvitesTheNewGuest_AndUpdatesTheRest()
    {
        var stale = new string('0', 64);
        var added = Invited.Replace("END:VEVENT", "ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:paul@example.org\r\nEND:VEVENT");

        var decision = SchedulingDecider.Decide(new SchedulingInput(
            new SchedulingBefore(Invited, "webmail", stale), added, WriteOrigin.Webmail, Alice));

        Assert.Equal(2, decision.Mails.Count);
        Assert.Equal(["paul@example.org"], decision.Mails.Single(m => m.Kind == MailKind.Invitation).Recipients);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"],
            decision.Mails.Single(m => m.Kind == MailKind.Update).Recipients.Order());
    }

    // Paul is added to the master (a file-level addition — its own Invitation); Julie leaves the
    // 19 Oct override alone (no file-level move, since she stays invited via the master). Both
    // have to reach the other, unaffected guests, not just Paul's own door.
    [Fact]
    public void GuestAddedAtFileLevel_AndAnotherRemovedFromOneOverrideOnly_ReportsBoth()
    {
        const string julieLine = "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n";
        var overrideIcs = Fixture("webmail-invited-override");
        var firstJulie = overrideIcs.IndexOf(julieLine, StringComparison.Ordinal);
        var secondJulie = overrideIcs.IndexOf(julieLine, firstJulie + julieLine.Length, StringComparison.Ordinal);
        var withoutOverrideJulie = overrideIcs.Remove(secondJulie, julieLine.Length);
        var firstEnd = withoutOverrideJulie.IndexOf("END:VEVENT", StringComparison.Ordinal);
        var after = withoutOverrideJulie.Insert(firstEnd, "ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:paul@example.org\r\n");

        var decision = Decide(overrideIcs, after, owner: "webmail");

        Assert.Equal(2, decision.Mails.Count);
        Assert.Equal(["paul@example.org"], decision.Mails.Single(m => m.Kind == MailKind.Invitation).Recipients);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"],
            decision.Mails.Single(m => m.Kind == MailKind.Update).Recipients.Order());
    }
}
