using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Dav;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Calendar.Invitations;
using weesky.Scotty.Microservice.Services.Calendar.Scheduling;
using weesky.Scotty.Microservice.Tests.Fixtures;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using weesky.Scotty.Microservice.Tests.Services.Calendar.Invitations;
using Xunit;
using CalendarEvent = weesky.Scotty.Microservice.Data.Preferences.CalendarEvent;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Scheduling;

/// <summary>The hook over the real stores and the real CalDAV writer (InMemory base), the mail side
/// mocked: what the calendar holds and what leaves after one write of either door.</summary>
public sealed class InvitationSchedulerStoreTests : IAsyncLifetime
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private const string Gmail = "alice.perso@gmail.com";

    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<IOrganizerIdentity> _organizer = new();
    private readonly Mock<IMailSender> _sender = new();
    private readonly Mock<IServiceMailQueue> _queue = new();
    private readonly Mock<IUserPreferenceStore> _preferences = new();
    private readonly List<MimeMessage> _sentBySession = [];
    private readonly List<QueuedMail> _queued = [];
    private User user = null!;
    private PreferencesTestDbContext context = null!;
    private CalendarEventStore events = null!;
    private DavCalendarWriter writer = null!;
    private Guid calendar;
    private InvitationScheduler scheduler = null!;

    public async Task InitializeAsync()
    {
        var database = Guid.NewGuid().ToString();
        var (_, userId, calendarId) = await CalendarStoreTestFactory.SeedAsync(database);
        calendar = calendarId;
        user = new User("alice@weesky.be") { WebmailUid = userId, FullName = "Alice" };
        context = new PreferencesTestDbContext(database);
        var sync = new TestCalendarSyncStore(context);
        events = new CalendarEventStore(context, sync, NullLogger<CalendarEventStore>.Instance);
        writer = new DavCalendarWriter(events, sync, context, NullLogger<DavCalendarWriter>.Instance);
        _addresses.Setup(a => a.ForPrimaryAsync(user, None)).ReturnsAsync(["alice@weesky.be", "alice@weesky.net"]);
        _addresses.Setup(a => a.ForPrincipalAsync(user, None)).ReturnsAsync(["alice@weesky.be", "alice@weesky.net", Gmail]);
        _organizer.Setup(o => o.ResolveAsync(user, None)).ReturnsAsync(new OrganizerWrite("alice@weesky.be", "Alice"));
        _sender.Setup(s => s.SendBuiltAsync(user, Conn, It.IsAny<MimeMessage>(), None))
            .Callback<User, MailAccountConnection, MimeMessage, CancellationToken>((_, _, m, _) => _sentBySession.Add(m))
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));
        _queue.Setup(q => q.TryEnqueue(It.IsAny<QueuedMail>())).Callback<QueuedMail>(_queued.Add).Returns(true);
        _preferences.Setup(p => p.GetAsync(userId, None))
            .ReturnsAsync([new UserPreference { PreferenceKey = UserPreferences.UiLanguage, PreferenceValue = "fr" }]);
        scheduler = new InvitationScheduler(events, writer, _addresses.Object, _organizer.Object, _sender.Object, _queue.Object,
            _preferences.Object, TimeProvider.System, NullLogger<InvitationScheduler>.Instance);
    }

    public Task DisposeAsync()
    {
        context.Dispose();
        return Task.CompletedTask;
    }

    private static string Fixture(string name) => InvitationParserTests.Fixture(name);

    private static Task<MailAccountConnection?> Session(CancellationToken _) => Task.FromResult<MailAccountConnection?>(Conn);

    /// <summary>The file put through the DAV writer under a name, so the row, its revisions and its
    /// sync rank exist as in production; the two columns stamped when an owner is given.</summary>
    private async Task<(string DavName, string Ics)> StoredAsync(string ics, string? owner = null, string? hash = null)
    {
        var name = $"{Guid.NewGuid()}.ics";
        Assert.Equal(DavWriteStatus.Created, (await writer.PutAsync(user.WebmailUid, calendar, name, ics, None)).Status);
        if (owner is not null) await events.SetSchedulingAsync(user.WebmailUid, calendar, name, owner, hash, None);
        return (name, ics);
    }

    private Task<CalendarEvent> RowAsync(string davName) =>
        context.CalendarEvents.AsNoTracking().SingleAsync(e => e.DavName == davName);

    private static string Hash(string ics) => SchedulingShape.HashOf(SchedulingShape.Of(ics)!);

    private static string CalendarPartOf(MimeMessage message) =>
        MimeWire.TextOf(Assert.IsType<MultipartAlternative>(Assert.IsType<Multipart>(message.Body)[0]).OfType<TextPart>().Last());

    [Fact]
    public async Task FirstInvitationFromTheWebmail_SendsBySession_AndStampsTheColumns()
    {
        var (name, ics) = await StoredAsync(Fixture("webmail-invited"));

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport("webmail", 2), report);
        var mail = Assert.Single(_sentBySession);
        Assert.Equal("Invitation\u00A0: Réunion de rentrée", mail.Subject);
        Assert.Equal(["julie@example.net", "marc.dupont@example.org"], mail.To.Mailboxes.Select(m => m.Address).Order());
        Assert.Empty(_queued);
        var row = await RowAsync(name);
        Assert.Equal(("webmail", Hash(ics)), (row.SchedulingOwner, row.SchedulingHash));
        Assert.Equal(0, InvitationParser.SequenceOf(row.IcsRaw));
    }

    // Spec § 9: an override moved alone advances alone. The device wrote a new override at
    // SEQUENCE:0 beside a master it did not advance, so the server gives the override SEQUENCE:1.
    [Fact]
    public async Task ADeviceMovingAnOverride_WithoutBumping_GetsASecondWrite_AndTheQueue()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var moved = Fixture("webmail-invited-override").Replace("SEQUENCE:1", "SEQUENCE:0");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, moved, None);
        var rankAfterDevice = put.Sequence;
        var revisionsAfterDevice = await context.CalendarRevisions.CountAsync();

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, moved), WriteOrigin.Device, null, null, None);

        Assert.Equal(new SchedulingReport("webmail", 2), report);
        Assert.Empty(_sentBySession);
        var queued = Assert.Single(_queued);
        Assert.Equal("Mise à jour\u00A0: Réunion de rentrée", queued.Message.Subject);
        var row = await RowAsync(name);
        Assert.Equal(0, InvitationParser.SequenceOf(row.IcsRaw));
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Brussels:20261019T100000\r\nDTSTAMP:20260913T100000Z\r\nSEQUENCE:1", row.IcsRaw);
        Assert.True(row.SyncSequence > rankAfterDevice);
        Assert.Equal(revisionsAfterDevice + 1, await context.CalendarRevisions.CountAsync());
        Assert.Equal(RevisionCause.Scheduling, (await context.CalendarRevisions.OrderByDescending(r => r.Id).FirstAsync()).Cause);
        Assert.Equal(Hash(moved), row.SchedulingHash);
        Assert.Contains("\r\nSEQUENCE:1\r\n", CalendarPartOf(queued.Message));
        Assert.Equal("Update web-1111-2222 seq 0 to julie@example.net, marc.dupont@example.org", queued.Description);
    }

    [Fact]
    public async Task ADeviceThatBumped_GetsNoSecondWrite()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var moved = Fixture("webmail-invited-override");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, moved, None);
        var revisions = await context.CalendarRevisions.CountAsync();

        await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, moved), WriteOrigin.Device, null, null, None);

        Assert.Equal(revisions, await context.CalendarRevisions.CountAsync());
        Assert.Equal(put.Sequence, (await RowAsync(name)).SyncSequence);
        Assert.Single(_queued);
    }

    // The second write is conditional on the bytes the hook was told about: a write that landed
    // between the two keeps its place, and its own hook call answers for it.
    [Fact]
    public async Task TheSecondWrite_NeverOverwritesAWriteThatLandedMeanwhile()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var moved = before.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, moved, None);
        var meanwhile = moved.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion déplacée");
        Assert.Equal(DavWriteStatus.Replaced, (await writer.PutAsync(user.WebmailUid, calendar, name, meanwhile, None)).Status);

        await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, moved), WriteOrigin.Device, null, null, None);

        Assert.Equal(meanwhile, (await RowAsync(name)).IcsRaw);
        Assert.Single(_queued);
    }

    [Fact]
    public async Task AnAnswerWrittenByAPhone_SendsNothing_AndKeepsTheColumns()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var answered = before.Replace("PARTSTAT=NEEDS-ACTION", "PARTSTAT=ACCEPTED");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, answered, None);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, answered), WriteOrigin.Device, null, null, None);

        Assert.Equal(new SchedulingReport("webmail", 0), report);
        Assert.Empty(_queued);
        var row = await RowAsync(name);
        Assert.Equal(("webmail", Hash(before)), (row.SchedulingOwner, row.SchedulingHash));
    }

    [Fact]
    public async Task ADeletion_CancelsEveryone_WithTheSequencePlusOne()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var outcome = await writer.DeleteAsync(user.WebmailUid, calendar, name, None);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, outcome.Replaced, null), WriteOrigin.Webmail, Session, "en", None);

        Assert.Equal(new SchedulingReport(null, 2), report);
        var mail = Assert.Single(_sentBySession);
        Assert.Equal("Cancelled: Réunion de rentrée", mail.Subject);
        Assert.Contains("SEQUENCE:1", CalendarPartOf(mail));
    }

    [Fact]
    public async Task AnEventNobodyOwns_WrittenByADevice_SendsNothing_AndReadsNothing()
    {
        var (name, ics) = await StoredAsync(Fixture("webmail-invited"));

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Device, null, null, None);

        Assert.Equal(new SchedulingReport(null, 0), report);
        Assert.Null((await RowAsync(name)).SchedulingOwner);
        _addresses.VerifyNoOtherCalls();
        _organizer.VerifyNoOtherCalls();
        _preferences.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AWebmailWriteWithoutGuests_OnAnEventNobodyOwns_ReadsNothing()
    {
        var alone = Fixture("webmail-invited")
            .Replace("ATTENDEE;CN=Marc Dupont;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:marc.dupont@example.org\r\n", "")
            .Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "");
        Assert.DoesNotContain("ATTENDEE", alone);
        var (name, ics) = await StoredAsync(alone);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport(null, 0), report);
        _addresses.VerifyNoOtherCalls();
        _organizer.VerifyNoOtherCalls();
        _preferences.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithoutASession_TheWebmailFallsBackToTheQueue_AndAFailedSendStillStampsTheColumns()
    {
        var (name, ics) = await StoredAsync(Fixture("webmail-invited"));
        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, _ => Task.FromResult<MailAccountConnection?>(null), "fr", None);
        Assert.Equal(new SchedulingReport("webmail", 2), report);
        Assert.Single(_queued);

        // A device's change the queue refuses (no service account): the mail is lost and logged,
        // never resent — the columns still say the invitees were owed this version (décision 10).
        _queued.Clear();
        _queue.Setup(q => q.TryEnqueue(It.IsAny<QueuedMail>())).Returns(false);
        var owned = Fixture("webmail-invited").Replace("web-1111-2222", "web-3333");
        var (other, _) = await StoredAsync(owned, "webmail", Hash(owned));
        var moved = owned.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var put = await writer.PutAsync(user.WebmailUid, calendar, other, moved, None);
        var failed = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, other, put.Replaced, moved), WriteOrigin.Device, null, null, None);
        Assert.Equal(new SchedulingReport("webmail", 0), failed);
        Assert.Equal(Hash(moved), (await RowAsync(other)).SchedulingHash);
    }

    // A device may write any ATTENDEE; one MimeKit refuses is skipped, and a mail whose sending
    // throws leaves the other mails of the same write, on a session opened once for all of them.
    [Fact]
    public async Task AMalformedGuest_IsSkipped_AndAFailingMail_NeverStopsTheOthers()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var after = before.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net",
            "ATTENDEE:mailto:@example.org\r\nATTENDEE:mailto:paul@example.com");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, after, None);
        Assert.Equal(DavWriteStatus.Replaced, put.Status);
        _sender.Setup(s => s.SendBuiltAsync(user, Conn, It.Is<MimeMessage>(m => m.Subject == "Invitation: Réunion de rentrée"), None))
            .ThrowsAsync(new InvalidOperationException("smtp"));
        var opened = 0;

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, after), WriteOrigin.Webmail,
            ct => { opened++; return Session(ct); }, "en", None);

        Assert.Equal(new SchedulingReport("webmail", 1), report);
        var cancelled = Assert.Single(_sentBySession);
        Assert.Equal("Cancelled: Réunion de rentrée", cancelled.Subject);
        Assert.Equal(["julie@example.net"], cancelled.To.Mailboxes.Select(m => m.Address));
        _sender.Verify(s => s.SendBuiltAsync(user, Conn,
            It.Is<MimeMessage>(m => m.Subject == "Invitation: Réunion de rentrée" && m.To.Mailboxes.Single().Address == "paul@example.com"), None), Times.Once);
        Assert.Equal(1, opened);
        Assert.Equal(Hash(after), (await RowAsync(name)).SchedulingHash);
    }

    // Spec § 9: the takeover follows the SEQUENCE rule too. Another client invited, the user retitles
    // it in the webmail, whose composer does not advance a title: the server does, and says "Mise à jour".
    [Fact]
    public async Task AWebmailTakeover_OfAnEventAnotherClientInvited_SendsAnUpdate_AtAnAdvancedSequence()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before);
        var retitled = before.Replace("SUMMARY:Réunion de rentrée", "SUMMARY:Réunion de rentrée reportée");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, retitled, None);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, retitled), WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport("webmail", 2), report);
        var mail = Assert.Single(_sentBySession);
        Assert.Equal("Mise à jour\u00A0: Réunion de rentrée reportée", mail.Subject);
        Assert.Contains("\r\nSEQUENCE:1\r\n", CalendarPartOf(mail));
        var row = await RowAsync(name);
        Assert.Equal(("webmail", Hash(retitled)), (row.SchedulingOwner, row.SchedulingHash));
        Assert.Equal(1, InvitationParser.SequenceOf(row.IcsRaw));
    }

    // 5e2 takeover: a webmail save that changes only the description takes ownership silently,
    // since nothing about it was ever mailed to a guest.
    [Fact]
    public async Task ATakeoverChangingOnlyTheDescription_SendsNothing_AndStampsTheColumns()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before);
        var withDescription = before.Replace("END:VEVENT", "DESCRIPTION:Apporter les slides\r\nEND:VEVENT");
        var revisionsBefore = await context.CalendarRevisions.CountAsync();
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, withDescription, None);

        var report = await scheduler.AfterWriteAsync(
            user, new EventChange(null, calendar, name, put.Replaced, withDescription), WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport("webmail", 0), report);
        Assert.Empty(_sentBySession);
        Assert.Empty(_queued);
        Assert.Equal(revisionsBefore + 1, await context.CalendarRevisions.CountAsync());
        var row = await RowAsync(name);
        Assert.Equal(("webmail", Hash(withDescription)), (row.SchedulingOwner, row.SchedulingHash));
        Assert.Equal(withDescription, row.IcsRaw);
    }

    // Once taken over, the row behaves like any other owned event: a later save moving the time
    // mails the guests an Update, exactly as it would for an event the webmail invited itself.
    // Chained on purpose: the takeover must actually happen first, on the same row.
    [Fact]
    public async Task AWebmailTakeover_ThenAnOwnedTimeMove_SendsAnUpdate()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before);
        var withDescription = before.Replace("END:VEVENT", "DESCRIPTION:Apporter les slides\r\nEND:VEVENT");
        var put1 = await writer.PutAsync(user.WebmailUid, calendar, name, withDescription, None);
        var takeover = await scheduler.AfterWriteAsync(
            user, new EventChange(null, calendar, name, put1.Replaced, withDescription), WriteOrigin.Webmail, Session, "fr", None);
        Assert.Equal(new SchedulingReport("webmail", 0), takeover);

        var moved = withDescription
            .Replace("DTSTART;TZID=Europe/Brussels:20261005T100000", "DTSTART;TZID=Europe/Brussels:20261005T140000")
            .Replace("DTEND;TZID=Europe/Brussels:20261005T110000", "DTEND;TZID=Europe/Brussels:20261005T150000");
        var put2 = await writer.PutAsync(user.WebmailUid, calendar, name, moved, None);

        var report = await scheduler.AfterWriteAsync(
            user, new EventChange(null, calendar, name, put2.Replaced, moved), WriteOrigin.Device, null, null, None);

        Assert.Equal(new SchedulingReport("webmail", 2), report);
        Assert.Equal("Mise à jour\u00A0: Réunion de rentrée", Assert.Single(_queued).Message.Subject);
        Assert.Equal(Hash(moved), (await RowAsync(name)).SchedulingHash);
    }

    // Main review gap: every other takeover test builds "after" by editing fixture text by hand.
    // This one pushes a file a real client wrote through the actual save path —
    // CalendarEventStore.UpdateAsync -> IcsComposer.RewriteAll — fed by an EventWrite built the way
    // the editor's writeOf(formOf(detail)) would: the stored fields unchanged, the guests sent back
    // exactly as read, only the description touched. If the composer moved the fingerprint on a
    // field the user never touched, this would wrongly mail an Update in production.
    [Fact]
    public async Task ATakeoverThroughTheRealComposer_ChangingOnlyTheDescription_SendsNothing()
    {
        // The stored resource, unlike the mail attachment this fixture doubles as elsewhere, never
        // carries METHOD (RFC 4791 § 4.1) — this is Thunderbird's calendar entry, not its invitation mail.
        var thunderbird = Fixture("thunderbird-request")
            .Replace("METHOD:REQUEST\r\n", "")
            .Replace("ORGANIZER;CN=Léa:mailto:lea@example.net", "ORGANIZER;CN=Alice:mailto:alice@weesky.net");
        var (name, _) = await StoredAsync(thunderbird);
        var row = await RowAsync(name);

        var detail = await events.GetAsync(user.WebmailUid, row.Id, None);
        Assert.NotNull(detail);
        var guests = detail!.Attendees.Where(a => !a.IsOrganizer && a.RecurrenceId is null)
            .Select(a => new AttendeeWrite(a.Email, a.Name)).ToList();
        var write = detail.Fields with { Description = "Ne pas oublier les diapositives", Attendees = guests };

        var updated = await events.UpdateAsync(
            user.WebmailUid, detail.Id, EditScope.All, null, write, detail.IcsHash, None);
        Assert.True(updated.IsSuccess);
        var change = Assert.Single(updated.Value.Changes);

        var report = await scheduler.AfterWriteAsync(user, change, WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport("webmail", 0), report);
        Assert.Empty(_sentBySession);
        Assert.Empty(_queued);
        var updatedRow = await RowAsync(name);
        Assert.Equal("webmail", updatedRow.SchedulingOwner);
        // Genuine round-trip, not a vacuous pass: the description landed, and the guest survived.
        Assert.Contains("Ne pas oublier les diapositives", updatedRow.IcsRaw);
        Assert.Contains("mailto:lea@example.net", updatedRow.IcsRaw);
    }

    // Review round 2: thunderbird-request has no TZID, RRULE or STATUS, so the round-trip of those
    // fingerprinted fields through the real composer was still untested. This fixture carries all three.
    [Fact]
    public async Task ATakeoverThroughTheRealComposer_OnARecurringConfirmedEvent_ChangingOnlyTheDescription_SendsNothing()
    {
        var before = Fixture("webmail-invited").Replace("SEQUENCE:0", "SEQUENCE:0\r\nSTATUS:CONFIRMED");
        var (name, _) = await StoredAsync(before);
        var row = await RowAsync(name);

        var detail = await events.GetAsync(user.WebmailUid, row.Id, None);
        Assert.NotNull(detail);
        var guests = detail!.Attendees.Where(a => !a.IsOrganizer && a.RecurrenceId is null)
            .Select(a => new AttendeeWrite(a.Email, a.Name)).ToList();
        var write = detail.Fields with { Description = "Apporter les slides", Attendees = guests };

        var updated = await events.UpdateAsync(
            user.WebmailUid, detail.Id, EditScope.All, null, write, detail.IcsHash, None);
        Assert.True(updated.IsSuccess);
        var change = Assert.Single(updated.Value.Changes);

        var report = await scheduler.AfterWriteAsync(user, change, WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport("webmail", 0), report);
        Assert.Empty(_sentBySession);
        Assert.Empty(_queued);
        var updatedRow = await RowAsync(name);
        Assert.Equal("webmail", updatedRow.SchedulingOwner);
        Assert.Contains("Apporter les slides", updatedRow.IcsRaw);
        Assert.Contains("mailto:marc.dupont@example.org", updatedRow.IcsRaw);
        Assert.Contains("RRULE:FREQ=WEEKLY;COUNT=4", updatedRow.IcsRaw);
    }

    // The door's write has committed when the hook runs: a device that hangs up right after its DELETE
    // still owes the invitees their CANCEL, and nothing would ever catch it up — the row is gone.
    [Fact]
    public async Task ADeviceDeletion_WhoseRequestWasAborted_StillQueuesTheCancel()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var outcome = await writer.DeleteAsync(user.WebmailUid, calendar, name, None);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, outcome.Replaced, null), WriteOrigin.Device, null, null,
            new CancellationToken(canceled: true));

        Assert.Equal(new SchedulingReport(null, 2), report);
        Assert.Equal("Annulation\u00A0: Réunion de rentrée", Assert.Single(_queued).Message.Subject);
    }

    // Only the user's own SMTP send answers to the request. Cancelled in flight, that mail and every one
    // after it go by the service queue, as without a session: a CANCEL nothing would re-send is never lost.
    [Fact]
    public async Task AnAbortedWebmailRequest_HandsTheMailInFlightAndTheRemainingCancelToTheQueue_AndStampsTheColumns()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var after = before.Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net", "ATTENDEE:mailto:paul@example.com");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, after, None);
        using var request = new CancellationTokenSource();
        _sender.Setup(s => s.SendBuiltAsync(user, Conn, It.IsAny<MimeMessage>(), request.Token))
            .Callback(request.Cancel)
            .ThrowsAsync(new OperationCanceledException(request.Token));

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, after), WriteOrigin.Webmail, Session, "en", request.Token);

        Assert.Equal(new SchedulingReport("webmail", 2), report);
        Assert.Equal(["Invitation: Réunion de rentrée", "Cancelled: Réunion de rentrée"], _queued.Select(q => q.Message.Subject));
        _sender.Verify(s => s.SendBuiltAsync(user, Conn, It.IsAny<MimeMessage>(), request.Token), Times.Once);
        Assert.Equal(("webmail", Hash(after)), ((await RowAsync(name)).SchedulingOwner, (await RowAsync(name)).SchedulingHash));
    }

    [Fact]
    public async Task AnAbortedWebmailDeletion_QueuesItsCancel()
    {
        var before = Fixture("webmail-invited");
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var outcome = await writer.DeleteAsync(user.WebmailUid, calendar, name, None);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, outcome.Replaced, null), WriteOrigin.Webmail, Session, "en",
            new CancellationToken(canceled: true));

        Assert.Equal(new SchedulingReport(null, 2), report);
        Assert.Equal("Cancelled: Réunion de rentrée", Assert.Single(_queued).Message.Subject);
        Assert.Empty(_sentBySession);
    }

    // No invitee held an earlier version: a first invitation to an event that already existed is sent
    // at the SEQUENCE the webmail wrote, without a second write.
    [Fact]
    public async Task AFirstInvitation_ToAnExistingEventWithoutGuests_WritesOnce()
    {
        var alone = Fixture("webmail-invited")
            .Replace("ATTENDEE;CN=Marc Dupont;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:marc.dupont@example.org\r\n", "")
            .Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net\r\n", "");
        var (name, _) = await StoredAsync(alone);
        var invited = Fixture("webmail-invited");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, invited, None);
        var revisions = await context.CalendarRevisions.CountAsync();

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, invited), WriteOrigin.Webmail, Session, "en", None);

        Assert.Equal(new SchedulingReport("webmail", 2), report);
        Assert.Equal("Invitation: Réunion de rentrée", Assert.Single(_sentBySession).Subject);
        Assert.Equal(revisions, await context.CalendarRevisions.CountAsync());
        var row = await RowAsync(name);
        Assert.Equal((put.Sequence, invited), (row.SyncSequence, row.IcsRaw));
    }

    // Through the hook, what InvitationMailer.Mailbox accepts is delivered — an IDN in punycode, a
    // trailing root dot — and the rest skipped: a group spelling, a bare word no RCPT would take.
    [Fact]
    public async Task EveryAddressTheComposerAccepts_IsDelivered_AndTheOthersSkipped()
    {
        var alone = Fixture("webmail-invited")
            .Replace("ATTENDEE;CN=Marc Dupont;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:marc.dupont@example.org", "ATTENDEE:mailto:user@xn--bcher-kva.de")
            .Replace("ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=ACCEPTED:mailto:Julie@Example.net",
                "ATTENDEE:mailto:user@example.com.\r\nATTENDEE:mailto:mailto:a@b.org\r\nATTENDEE:mailto:foo");
        var (name, ics) = await StoredAsync(alone);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, Session, "en", None);

        Assert.Equal(new SchedulingReport("webmail", 2), report);
        Assert.Equal(["user@bücher.de", "user@example.com"], Assert.Single(_sentBySession).To.Mailboxes.Select(m => m.Address).Order());
    }

    // The toast says "invitations sent: N": one message carries every recipient of its kind, so N counts people.
    // The user's Gmail is a guest like any other: only the primary account's addresses are the organizer's own.
    [Fact]
    public async Task AGuestThatIsAConnectedAccountsIdentity_IsInvited_AndEveryRecipientCounted()
    {
        var withGmail = Fixture("webmail-invited").Replace("END:VEVENT", $"ATTENDEE;PARTSTAT=NEEDS-ACTION;RSVP=TRUE:mailto:{Gmail}\r\nEND:VEVENT");
        var (name, ics) = await StoredAsync(withGmail);

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, Session, "en", None);

        Assert.Equal(new SchedulingReport("webmail", 3), report);
        Assert.Equal([Gmail, "julie@example.net", "marc.dupont@example.org"], Assert.Single(_sentBySession).To.Mailboxes.Select(m => m.Address).Order());
    }

    // From is the address replies come back to, and the service account may only speak for the primary account:
    // a file's ORGANIZER outside the primary list (a device may write any) leaves under the resolved identity.
    [Theory]
    [InlineData("ORGANIZER;CN=Alice Net:mailto:Alice@weesky.net", "alice@weesky.net", "Alice Net", false)]
    [InlineData("ORGANIZER;CN=Alice Perso:mailto:" + Gmail, "alice@weesky.be", "Alice", true)]
    [InlineData("ORGANIZER:mailto:boss@example.org", "alice@weesky.be", "Alice", true)]
    public async Task TheSender_IsTheFilesOrganizer_OnlyWhenItIsAPrimaryAddress(string organizerLine, string address, string displayName, bool warns)
    {
        var before = Fixture("webmail-invited").Replace("ORGANIZER;CN=Alice:mailto:alice@weesky.be", organizerLine);
        var (name, _) = await StoredAsync(before, "webmail", Hash(before));
        var moved = before.Replace("LOCATION:Salle 2", "LOCATION:Salle 3");
        var put = await writer.PutAsync(user.WebmailUid, calendar, name, moved, None);
        var logger = new Mock<ILogger<InvitationScheduler>>();
        var sut = new InvitationScheduler(events, writer, _addresses.Object, _organizer.Object, _sender.Object, _queue.Object,
            _preferences.Object, TimeProvider.System, logger.Object);

        await sut.AfterWriteAsync(user, new EventChange(null, calendar, name, put.Replaced, moved), WriteOrigin.Device, null, null, None);

        var from = Assert.Single(Assert.Single(_queued).Message.From.Mailboxes);
        Assert.Equal((address, displayName), (from.Address, from.Name));
        if (warns) logger.VerifyWarningLoggedContaining("is not one of the primary account's addresses");
        else logger.VerifyNoWarningLogged();
    }

    [Fact]
    public async Task NeverThrows()
    {
        _addresses.Setup(a => a.ForPrimaryAsync(user, None)).ThrowsAsync(new InvalidOperationException("boom"));
        var (name, ics) = await StoredAsync(Fixture("webmail-invited"));

        var report = await scheduler.AfterWriteAsync(user, new EventChange(null, calendar, name, null, ics), WriteOrigin.Webmail, Session, "fr", None);

        Assert.Equal(new SchedulingReport(null, 0), report);
    }
}
