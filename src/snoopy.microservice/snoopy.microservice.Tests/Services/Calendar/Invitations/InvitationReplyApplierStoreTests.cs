using System.Text;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Services.Calendar.Scheduling;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

/// <summary>The reply applier over the real stores and the real CalDAV writer (InMemory base), the
/// mailbox mocked: what the stored file and its columns hold once a guest's answer is applied.</summary>
public sealed class InvitationReplyApplierStoreTests : IAsyncLifetime
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private static readonly ApplyReplyRequest Request = new() { Folder = "INBOX", Uid = 7, Part = "2" };

    private readonly string database = Guid.NewGuid().ToString();
    private readonly Mock<IMailMessageRepository> _messages = new();
    private PreferencesTestDbContext context = null!;
    private CalendarEventStore events = null!;
    private DavCalendarWriter writer = null!;
    private InvitationReplyApplier applier = null!;
    private User user = null!;
    private Guid calendar;

    public async Task InitializeAsync()
    {
        var (_, userId, calendarId) = await CalendarStoreTestFactory.SeedAsync(database);
        (user, calendar) = (new User("alice@weesky.be") { WebmailUid = userId }, calendarId);
        context = new PreferencesTestDbContext(database);
        var sync = new TestCalendarSyncStore(context);
        events = new CalendarEventStore(context, sync, NullLogger<CalendarEventStore>.Instance);
        writer = new DavCalendarWriter(events, sync, context, NullLogger<DavCalendarWriter>.Instance);
        // Strict: a REPLY names the guest, never the user, so the user's addresses are never asked.
        applier = new InvitationReplyApplier(new InvitationPartLoader(_messages.Object),
            new InvitationReader(Mock.Of<IUserAddresses>(MockBehavior.Strict), events), writer,
            NullLogger<InvitationReplyApplier>.Instance);
    }

    public Task DisposeAsync()
    {
        context.Dispose();
        return Task.CompletedTask;
    }

    private async Task<StoredEventRef> InvitedAsync(
        string? owner = "webmail", string fixture = "webmail-invited", Func<string, string>? edit = null)
    {
        var name = $"{Guid.NewGuid()}.ics";
        var ics = InvitationParserTests.Fixture(fixture);
        await writer.PutAsync(user.WebmailUid, calendar, name, edit is null ? ics : edit(ics), None);
        if (owner is not null) await events.SetSchedulingAsync(user.WebmailUid, calendar, name, owner, "h", None);
        return (await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None)).Single();
    }

    private static string ReplyIcs(string fixture, Func<string, string>? edit = null)
    {
        var ics = InvitationParserTests.Fixture(fixture).Replace("aaaa1111-bbbb-2222-cccc-3333dddd4444", "web-1111-2222");
        return edit is null ? ics : edit(ics);
    }

    private void Part(string fixture, Func<string, string>? edit = null)
    {
        var ics = ReplyIcs(fixture, edit);
        _messages.Setup(m => m.GetAttachmentAsync(user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(() => Result.Success(new MailAttachmentContent
            {
                Content = new MemoryStream(Encoding.UTF8.GetBytes(ics)), ContentType = "text/calendar",
            }));
    }

    private static Func<string, string> Answer(string partStat, string dtStamp) =>
        ics => ics.Replace("PARTSTAT=DECLINED", "PARTSTAT=" + partStat).Replace("DTSTAMP:20260913T100000Z", "DTSTAMP:" + dtStamp);

    private static List<string> MarcsLines(string ics) => PartStatRewriter.Unfold(ics).Select(l => l.Text)
        .Where(l => l.EndsWith(":mailto:marc.dupont@example.org", StringComparison.Ordinal)).ToList();

    private async Task<StoredEventRef> RowAsync() => (await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None)).Single();

    [Fact]
    public async Task Apply_WritesTheGuestsAnswer_IntoTheStoredFile_UnderItsOwnName_AndIsIdempotent()
    {
        var stored = await InvitedAsync();
        Part("google-reply");

        var first = (await applier.ApplyAsync(user, Conn, Request, None)).Value;

        Assert.True(first.Applied);
        Assert.Null(first.ApplyError);
        Assert.True(first.Invitation.Reply!.Applied);
        Assert.Equal("DECLINED", first.Invitation.SavedPartStat);
        var row = await context.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == stored.Id);
        Assert.Equal(stored.DavName, row.DavName);
        Assert.Equal("DECLINED", InvitationParser.PartStatOf(row.IcsRaw, "marc.dupont@example.org"));
        Assert.Equal("ACCEPTED", InvitationParser.PartStatOf(row.IcsRaw, "julie@example.net"));
        Assert.Equal(("webmail", "h"), (row.SchedulingOwner, row.SchedulingHash));
        Assert.Equal(RevisionCause.Webmail, (await context.CalendarRevisions.OrderByDescending(r => r.Id).FirstAsync()).Cause);
        Assert.EndsWith(";PARTSTAT=DECLINED;X-WEESKY-REPLY-STAMP=20260913T100000Z:mailto:marc.dupont@example.org", Assert.Single(MarcsLines(row.IcsRaw)));

        var revisions = await context.CalendarRevisions.CountAsync();
        var rank = row.SyncSequence;
        var second = (await applier.ApplyAsync(user, Conn, Request, None)).Value;
        Assert.True(second.Applied);
        Assert.Equal(revisions, await context.CalendarRevisions.CountAsync());
        Assert.Equal(rank, (await context.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == stored.Id)).SyncSequence);
    }

    [Fact]
    public async Task Apply_MatchesAnUppercaseAddress_WithoutChangingTheShape()
    {
        await InvitedAsync();
        Part("outlook-reply-upper");
        var before = SchedulingShape.Of((await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None)).Single().IcsRaw);

        var applied = (await applier.ApplyAsync(user, Conn, Request, None)).Value;

        Assert.True(applied.Applied);
        var after = (await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None)).Single().IcsRaw;
        Assert.Equal("TENTATIVE", InvitationParser.PartStatOf(after, "marc.dupont@example.org"));
        Assert.Equal(before, SchedulingShape.Of(after));
    }

    [Fact]
    public async Task Apply_ToASeries_AnswersTheMasterAndTheOverride_EvenWhenTheMasterAlreadyHoldsIt()
    {
        var stored = await InvitedAsync(fixture: "webmail-invited-override",
            edit: ics => new Regex("PARTSTAT=NEEDS-ACTION").Replace(ics, "PARTSTAT=DECLINED", 1));
        Part("google-reply", ics => ics.Replace("SEQUENCE:0", "SEQUENCE:1"));
        var revisions = await context.CalendarRevisions.CountAsync();

        var applied = (await applier.ApplyAsync(user, Conn, Request, None)).Value;

        Assert.Equal((true, true), (applied.Applied, applied.Invitation.Reply!.Applied));
        Assert.Equal(revisions + 1, await context.CalendarRevisions.CountAsync());
        var row = await context.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == stored.Id);
        var marc = MarcsLines(row.IcsRaw);
        Assert.Equal(2, marc.Count);
        Assert.All(marc, l => Assert.Contains(";PARTSTAT=DECLINED;X-WEESKY-REPLY-STAMP=20260913T100000Z:", l));
        Assert.DoesNotContain("NEEDS-ACTION", row.IcsRaw);
    }

    [Theory]
    [InlineData(ReplyStatus.OccurrenceOnly)]
    [InlineData(ReplyStatus.Stale)]
    [InlineData(ReplyStatus.UnknownAttendee)]
    [InlineData(ReplyStatus.Superseded)]
    [InlineData(ReplyStatus.UnsupportedAnswer)]
    public async Task Apply_RefusesWhatTheReaderSaysIsNotApplicable_AndWritesNothing(ReplyStatus status)
    {
        var stored = await InvitedAsync(edit: status switch
        {
            ReplyStatus.Stale => ics => ics.Replace("SEQUENCE:0", "SEQUENCE:2"),
            ReplyStatus.Superseded => ics => PartStatRewriter.Rewrite(ics, "marc.dupont@example.org", "ACCEPTED", "20260915T090000Z")!,
            _ => null,
        });
        var reply = status switch
        {
            ReplyStatus.OccurrenceOnly => ReplyIcs("google-reply-occurrence"),
            ReplyStatus.UnknownAttendee => ReplyIcs("google-reply", ics => ics.Replace("marc.dupont@example.org", "paul@example.org")),
            ReplyStatus.UnsupportedAnswer => ReplyIcs("google-reply", Answer("DELEGATED", "20260913T100000Z")),
            _ => ReplyIcs("google-reply"),
        };
        Part("google-reply", _ => reply);
        var revisions = await context.CalendarRevisions.CountAsync();

        var result = await applier.ApplyAsync(user, Conn, Request, None);

        Assert.Equal((400, "reply_not_applicable"), (result.Error.Status, result.Error.Message));
        var read = await new InvitationReader(Mock.Of<IUserAddresses>(MockBehavior.Strict), events)
            .ReadAsync(user, Conn, new MailCalendarPart("2", reply, false), None);
        Assert.Equal(status, read!.Reply!.Status);
        Assert.Equal(revisions, await context.CalendarRevisions.CountAsync());
        Assert.Equal(stored.IcsRaw, (await RowAsync()).IcsRaw);
    }

    [Fact]
    public async Task Apply_TheLaterReplyWins_WhenItIsOpenedFirst()
    {
        var shape = SchedulingShape.Of((await InvitedAsync()).IcsRaw);
        var monday = ReplyIcs("google-reply", Answer("ACCEPTED", "20260914T090000Z"));
        Part("google-reply", Answer("DECLINED", "20260915T090000Z"));
        Assert.True((await applier.ApplyAsync(user, Conn, Request, None)).Value.Applied);

        Part("google-reply", _ => monday);
        var older = await applier.ApplyAsync(user, Conn, Request, None);

        Assert.Equal((400, "reply_not_applicable"), (older.Error.Status, older.Error.Message));
        var row = await RowAsync();
        Assert.Equal("DECLINED", InvitationParser.PartStatOf(row.IcsRaw, "marc.dupont@example.org"));
        Assert.Equal(shape, SchedulingShape.Of(row.IcsRaw));
        var read = await new InvitationReader(Mock.Of<IUserAddresses>(MockBehavior.Strict), events)
            .ReadAsync(user, Conn, new MailCalendarPart("2", monday, false), None);
        Assert.Equal(ReplyStatus.Superseded, read!.Reply!.Status);
    }

    [Fact]
    public async Task Apply_TheLaterReplyWins_WhenItIsOpenedLast_AndItsStampReplacesTheEarlierOne()
    {
        var shape = SchedulingShape.Of((await InvitedAsync()).IcsRaw);
        Part("google-reply", Answer("ACCEPTED", "20260914T090000Z"));
        Assert.True((await applier.ApplyAsync(user, Conn, Request, None)).Value.Applied);

        Part("google-reply", Answer("DECLINED", "20260915T090000Z"));
        var later = (await applier.ApplyAsync(user, Conn, Request, None)).Value;

        Assert.True(later.Applied);
        var row = await RowAsync();
        Assert.EndsWith(";PARTSTAT=DECLINED;X-WEESKY-REPLY-STAMP=20260915T090000Z:mailto:marc.dupont@example.org", Assert.Single(MarcsLines(row.IcsRaw)));
        Assert.Equal(shape, SchedulingShape.Of(row.IcsRaw));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_WhenTheEventChangesOrGoesBeforeTheWrite_NeitherOverwritesNorRecreatesIt(bool deleted)
    {
        var stored = await InvitedAsync();
        var moved = stored.IcsRaw.Replace("LOCATION:Salle 2", "LOCATION:Salle 5");
        Part("google-reply");
        var interleaving = new Mock<IDavCalendarWriter>();
        interleaving.Setup(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<RevisionCause>()))
            .Returns(async (Guid owner, Guid calendarId, string name, string ics, CancellationToken ct, bool createOnly, string? ifMatch, RevisionCause cause) =>
            {
                if (deleted) await writer.DeleteAsync(owner, calendarId, name, ct);
                else await writer.PutAsync(owner, calendarId, name, moved, ct);
                return await writer.PutAsync(owner, calendarId, name, ics, ct, createOnly, ifMatch, cause);
            });
        var racing = new InvitationReplyApplier(new InvitationPartLoader(_messages.Object),
            new InvitationReader(Mock.Of<IUserAddresses>(MockBehavior.Strict), events), interleaving.Object,
            NullLogger<InvitationReplyApplier>.Instance);

        var result = (await racing.ApplyAsync(user, Conn, Request, None)).Value;

        Assert.Equal((false, "calendar_conflict"), (result.Applied, result.ApplyError));
        var rows = await events.FindByUidAsync(user.WebmailUid, "web-1111-2222", None);
        if (deleted) Assert.Empty(rows);
        else Assert.Equal(moved, Assert.Single(rows).IcsRaw);
    }

    [Fact]
    public async Task Apply_OnAnEventNotInvitedHere_OrUnknown_Is400_AndWritesNothing()
    {
        Part("google-reply");
        Assert.Equal("reply_not_applicable", (await applier.ApplyAsync(user, Conn, Request, None)).Error.Message);

        var stored = await InvitedAsync(owner: null);
        var revisions = await context.CalendarRevisions.CountAsync();
        Assert.Equal("reply_not_applicable", (await applier.ApplyAsync(user, Conn, Request, None)).Error.Message);
        Assert.Equal(revisions, await context.CalendarRevisions.CountAsync());
        Assert.Equal(stored.IcsRaw, (await context.CalendarEvents.AsNoTracking().SingleAsync(e => e.Id == stored.Id)).IcsRaw);
    }

    [Fact]
    public async Task Apply_AReplyNamingNoGuest_Is400()
    {
        await InvitedAsync();
        Part("google-reply", ics => ics.Replace(
            "ATTENDEE;CUTYPE=INDIVIDUAL;ROLE=REQ-PARTICIPANT;PARTSTAT=DECLINED;CN=Marc Dupont:mailto:marc.dupont@example.org\r\n", ""));
        var result = await applier.ApplyAsync(user, Conn, Request, None);
        Assert.Equal((400, "reply_not_applicable"), (result.Error.Status, result.Error.Message));
    }

    [Fact]
    public async Task Apply_OnARequest_Is400()
    {
        Part("google-request");
        var result = await applier.ApplyAsync(user, Conn, Request, None);
        Assert.Equal((400, "reply_not_a_reply"), (result.Error.Status, result.Error.Message));
    }
}
