using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationResponderTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly Guid Personal = Guid.NewGuid();
    private static readonly Guid Work = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly User _user = new("alice@weesky.be") { WebmailUid = WebmailUid };
    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<ICalendarEventStore> _events = new();
    private readonly Mock<ICalendarStore> _calendars = new();
    private readonly Mock<IDavCalendarWriter> _writer = new();
    private readonly Mock<IMailSender> _sender = new();
    private readonly Mock<IRoleFolderLocator> _locator = new();
    private readonly Mock<IProfileReader> _profiles = new();

    private static string Fixture(string name) => InvitationParserTests.Fixture(name);

    private InvitationResponder Create()
    {
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, None)).ReturnsAsync(["alice@weesky.be"]);
        _events.Setup(e => e.FindByUidAsync(WebmailUid, It.IsAny<string>(), None)).ReturnsAsync([]);
        _calendars.Setup(c => c.ListAsync(WebmailUid, None)).ReturnsAsync([
            new CalendarView(Work, "work", "Travail", "", "#00f", 0, "Europe/Brussels", true, false),
            new CalendarView(Personal, "default", "Personnel", "", "#0f0", 1, "Europe/Brussels", true, true),
        ]);
        _writer.Setup(w => w.PutAsync(WebmailUid, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Created, "\"e\"", null, 1));
        _writer.Setup(w => w.DeleteAsync(WebmailUid, It.IsAny<Guid>(), It.IsAny<string>(), None, null))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Deleted, null, null, 2));
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));
        _locator.Setup(l => l.FindAsync(_user, Conn, "trash", None)).ReturnsAsync("Trash");
        _messages.Setup(m => m.MoveOrCopyAsync(_user, Conn, "INBOX", It.IsAny<IReadOnlyList<uint>>(), "Trash", false, None))
            .ReturnsAsync(Result.Success());
        _profiles.Setup(p => p.GetDisplayNameAsync(_user, None)).ReturnsAsync("Alice Martin");
        Part("google-request");
        return new InvitationResponder(_messages.Object, new InvitationReader(_addresses.Object, _events.Object),
            _calendars.Object, _writer.Object, _sender.Object, _locator.Object, _profiles.Object,
            NullLogger<InvitationResponder>.Instance);
    }

    private void Part(string fixture) => PartText(Fixture(fixture));

    private void PartText(string ics) => PartBytes(System.Text.Encoding.UTF8.GetBytes(ics), null);

    private void PartBytes(byte[] bytes, string? charset) =>
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(() => Result.Success(new MailAttachmentContent
            {
                Content = new MemoryStream(bytes), ContentType = "text/calendar", Charset = charset,
            }));

    /// <summary>A stored file carries no METHOD, so the parser would call it no invitation at all:
    /// its UID is read off the component, the way the reader itself reads it.</summary>
    private void Stored(string ics, Guid calendar, string davName = "phone-name.ics") =>
        _events.Setup(e => e.FindByUidAsync(WebmailUid, IcsDocument.Components(IcsDocument.TryLoad(ics)!).First().Uid!, None))
            .ReturnsAsync([new StoredEventRef(Guid.NewGuid(), calendar, davName, ics)]);

    private static RespondInvitationRequest Request(InvitationAnswer answer, Guid? calendarId = null) => new()
    {
        Folder = "INBOX", Uid = 7, Part = "2", Answer = answer, CalendarId = calendarId, Language = "fr", TimeZone = "Europe/Brussels",
    };

    // ── writing ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Accepted_WritesTheOrganizersFileWithThePartStat_InTheDefaultCalendar_UnderAFreshName()
    {
        var sut = Create();
        string? written = null; Guid? calendar = null; string? name = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, c, n, ics, _, _, _, _) => { calendar = c; name = n; written = ics; })
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Created, "\"e\"", null, 1));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Personal, calendar);
        Assert.EndsWith(".ics", name);
        Assert.Equal(PartStatRewriter.Rewrite(Fixture("google-request"), "alice@weesky.be", "ACCEPTED"), written);
        Assert.True(result.Value.ReplySent);
        Assert.False(result.Value.Trashed);
    }

    [Fact]
    public async Task Accepted_WithACalendarId_CreatesThere()
    {
        var sut = Create();
        await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted, Work), None);
        _writer.Verify(w => w.PutAsync(WebmailUid, Work, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail), Times.Once);
    }

    [Fact]
    public async Task Accepted_WithACalendarIdOfAnotherUser_FallsBackToTheDefault()
    {
        var sut = Create();
        await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted, Guid.NewGuid()), None);
        _writer.Verify(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail), Times.Once);
    }

    [Fact]
    public async Task AddOnly_WithNoCalendarAtAll_Is502()
    {
        var sut = Create();
        _calendars.Setup(c => c.ListAsync(WebmailUid, None)).ReturnsAsync([]);

        var failure = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.AddOnly), None)).Error;

        Assert.Equal(502, failure.Status);
        Assert.Equal(InvitationResponder.NoCalendar, failure.Message);
    }

    [Fact]
    public async Task Tentative_OnACurrentEvent_RewritesTheStoredFileUnderItsOwnName_AndIgnoresCalendarId()
    {
        var sut = Create();
        // Stored by a phone: its own name, its own alarm, the user already ACCEPTED.
        var stored = PartStatRewriter.Rewrite(Fixture("google-request"), "alice@weesky.be", "ACCEPTED")!
            .Replace("END:VEVENT", "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT10M\r\nEND:VALARM\r\nEND:VEVENT");
        Stored(stored, Personal, "phone-name.ics");
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, "phone-name.ics", It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Replaced, "\"f\"", null, 2));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Tentative, Work), None);

        Assert.True(result.IsSuccess);
        Assert.Contains("TRIGGER:-PT10M", written);
        // Read rather than matched on the text: RFC 5545 folds the rewritten ATTENDEE at 75 octets,
        // which falls inside "PARTSTAT=TENTATIVE".
        Assert.Equal("TENTATIVE", InvitationParser.PartStatOf(written!, "alice@weesky.be"));
        _writer.Verify(w => w.PutAsync(WebmailUid, Work, It.IsAny<string>(), It.IsAny<string>(), None, false, null, It.IsAny<RevisionCause>()), Times.Never);
    }

    [Fact]
    public async Task Accepted_OnAnOutdatedEvent_WritesTheReceivedFile_UnderTheStoredName()
    {
        var sut = Create();
        Stored(Fixture("google-request"), Work, "phone-name.ics");
        PartText(Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:2").Replace("T193000", "T200000"));
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Work, "phone-name.ics", It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Replaced, "\"f\"", null, 2));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.Contains("SEQUENCE:2", written);
        Assert.Contains("T200000", written);
    }

    [Fact]
    public async Task Declined_DeletesWhenPresent_SendsTheReply_AndTrashesTheMail()
    {
        var sut = Create();
        Stored(Fixture("google-request"), Personal);

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.DeleteAsync(WebmailUid, Personal, "phone-name.ics", None, null), Times.Once);
        _writer.Verify(w => w.PutAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), None, It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<RevisionCause>()), Times.Never);
        Assert.True(result.Value.ReplySent);
        Assert.True(result.Value.Trashed);
    }

    [Fact]
    public async Task Declined_WhenAbsent_WritesNothing_StillReplies()
    {
        var sut = Create();
        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.True(result.IsSuccess);
        _writer.VerifyNoOtherCalls();
        Assert.True(result.Value.ReplySent);
        Assert.True(result.Value.Trashed);
    }

    [Fact]
    public async Task Remove_WhenAbsent_Is400()
    {
        var sut = Create();
        Part("google-cancel");

        var failure = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Remove), None)).Error;

        Assert.Equal(400, failure.Status);
        Assert.Equal(InvitationResponder.NotInCalendar, failure.Message);
    }

    [Fact]
    public async Task AddOnly_StoresTheFileUntouchedButMethod_AndSendsNothing()
    {
        var sut = Create();
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, None)).ReturnsAsync(["someone@weesky.be"]);
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Created, "\"e\"", null, 1));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.AddOnly), None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PartStatRewriter.StripMethod(Fixture("google-request")), written);
        Assert.False(result.Value.ReplySent);
        Assert.Null(result.Value.ReplyError);
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AddOnly_OnACurrentEvent_WritesTheStoredFile_SoAPhonesAlarmSurvives()
    {
        var sut = Create();
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, None)).ReturnsAsync(["someone@weesky.be"]);
        var stored = PartStatRewriter.StripMethod(Fixture("google-request"))
            .Replace("END:VEVENT", "BEGIN:VALARM\r\nACTION:DISPLAY\r\nTRIGGER:-PT10M\r\nEND:VALARM\r\nEND:VEVENT");
        Stored(stored, Personal, "phone-name.ics");
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, "phone-name.ics", It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Replaced, "\"f\"", null, 2));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.AddOnly), None);

        Assert.True(result.IsSuccess);
        Assert.Equal(stored, written);
    }

    [Fact]
    public async Task AnAnswerOutsideTheEnum_Is400_AndWritesNothing()
    {
        var sut = Create();
        var failure = (await sut.RespondAsync(_user, Conn, Request((InvitationAnswer)9), None)).Error;

        Assert.Equal(400, failure.Status);
        Assert.Equal(InvitationResponder.UnknownAnswer, failure.Message);
        _writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Remove_OnACancel_DeletesTheStoredEvent_AndSendsNothing()
    {
        var sut = Create();
        Part("google-cancel");
        Stored(Fixture("google-request"), Personal);

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Remove), None);

        Assert.True(result.IsSuccess);
        _writer.Verify(w => w.DeleteAsync(WebmailUid, Personal, "phone-name.ics", None, null), Times.Once);
        _sender.VerifyNoOtherCalls();
        Assert.False(result.Value.Trashed);
    }

    // ── the reply ───────────────────────────────────────────────────────

    [Fact]
    public async Task TheReply_GoesFromAddressedToToTheOrganizer_WithTheUsersName()
    {
        var sut = Create();
        MimeMessage? sent = null;
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .Callback<User, MailAccountConnection, MimeMessage, CancellationToken>((_, _, m, _) => sent = m)
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));

        await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.Equal("alice@weesky.be", sent!.From.Mailboxes.Single().Address);
        Assert.Equal("Alice Martin", sent.From.Mailboxes.Single().Name);
        Assert.Equal("marc.dupont@example.org", sent.To.Mailboxes.Single().Address);
        Assert.Equal("Accepté : Dîner chez Marc", sent.Subject);
    }

    [Fact]
    public async Task ASendFailureAfterAcceptance_Is200WithReplySentFalse_TheEventStays()
    {
        var sut = Create();
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .ReturnsAsync(Result.Failure<SendMessageResult>("smtp_down"));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ReplySent);
        Assert.Equal("smtp_down", result.Value.ReplyError);
        _writer.Verify(w => w.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), None, null), Times.Never);
    }

    [Fact]
    public async Task ASendFailureOnADecline_LeavesTheMailWhereItIs()
    {
        var sut = Create();
        _sender.Setup(s => s.SendBuiltAsync(_user, Conn, It.IsAny<MimeMessage>(), None))
            .ReturnsAsync(Result.Failure<SendMessageResult>("smtp_down"));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.False(result.Value.Trashed);
        _messages.Verify(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), It.IsAny<bool>(), None), Times.Never);
    }

    [Fact]
    public async Task ATrashMoveThatFails_IsTrashedFalse_NothingElseUndone()
    {
        var sut = Create();
        _messages.Setup(m => m.MoveOrCopyAsync(_user, Conn, "INBOX", It.IsAny<IReadOnlyList<uint>>(), "Trash", false, None))
            .ReturnsAsync(Result.Failure("imap_down"));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ReplySent);
        Assert.False(result.Value.Trashed);
    }

    [Fact]
    public async Task ATrashFolderEqualToTheSourceFolder_MovesNothing()
    {
        var sut = Create();
        _locator.Setup(l => l.FindAsync(_user, Conn, "trash", None)).ReturnsAsync("INBOX");

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Declined), None);

        Assert.False(result.Value.Trashed);
        _messages.Verify(m => m.MoveOrCopyAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), It.IsAny<bool>(), None), Times.Never);
    }

    [Fact]
    public async Task NoOrganizer_IsReplySentFalse_WithAReason()
    {
        var sut = Create();
        PartText(Fixture("google-request").Replace("ORGANIZER;CN=Marc Dupont:mailto:marc.dupont@example.org\r\n", ""));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ReplySent);
        Assert.Equal(InvitationResponder.NoOrganizer, result.Value.ReplyError);
    }

    // ── refusals ────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFileIsReadFromImap_NeverFromTheRequest()
    {
        var sut = Create();
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.Equal(404, result.Error.Status);
    }

    [Fact]
    public async Task ImapUnreachable_Is502()
    {
        var sut = Create();
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(Result.Failure<MailAttachmentContent>("Unable to read the attachment"));

        Assert.Equal(502, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AnEmptyPartSpecifier_IsStillReadFromImap()
    {
        var sut = Create();
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "", None))
            .ReturnsAsync(() => Result.Success(new MailAttachmentContent
            {
                Content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Fixture("google-request"))), ContentType = "text/calendar",
            }));
        var request = Request(InvitationAnswer.Accepted);
        request.Part = "";

        var result = await sut.RespondAsync(_user, Conn, request, None);

        Assert.True(result.IsSuccess);
        Assert.Equal("", result.Value.Invitation.Part);
    }

    [Fact]
    public async Task ABomdPart_ReadsAsTheCardReadIt_NotAs422()
    {
        var sut = Create();
        PartBytes(System.Text.Encoding.UTF8.GetBytes("\uFEFF" + Fixture("google-request")), null);

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Dîner chez Marc", result.Value.Invitation.Summary);
    }

    [Fact]
    public async Task ALatin1Part_KeepsItsAccents_InTheCalendarAndTheReply()
    {
        var sut = Create();
        PartBytes(System.Text.Encoding.Latin1.GetBytes(Fixture("google-request")), "iso-8859-1");
        string? written = null;
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Created, "\"e\"", null, 1));

        var result = await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Dîner chez Marc", result.Value.Invitation.Summary);
        Assert.Contains("SUMMARY:Dîner chez Marc", written);
    }

    [Fact]
    public async Task AnAnswerForeignToTheMethod_Is400()
    {
        var sut = Create();
        var failure = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Remove), None)).Error;
        Assert.Equal(400, failure.Status);
        Assert.Equal(InvitationResponder.Incompatible, failure.Message);

        Part("google-cancel");
        Assert.Equal(400, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AnOccurrenceAlone_Is400()
    {
        var sut = Create();
        Part("occurrence-cancel");

        var failure = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Remove), None)).Error;

        Assert.Equal(400, failure.Status);
        Assert.Equal(InvitationResponder.OccurrenceOnly, failure.Message);
    }

    [Fact]
    public async Task AnsweringAForwardedInvitation_Is400_AddOnlyIsTheWay()
    {
        var sut = Create();
        _addresses.Setup(a => a.ForAccountAsync(_user, Conn, None)).ReturnsAsync(["someone@weesky.be"]);

        var failure = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error;

        Assert.Equal(400, failure.Status);
        Assert.Equal(InvitationResponder.NotAddressed, failure.Message);
    }

    [Fact]
    public async Task AnOlderSequenceThanStored_Is409()
    {
        var sut = Create();
        Stored(Fixture("google-request").Replace("SEQUENCE:0", "SEQUENCE:3"), Personal);

        var failure = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error;

        Assert.Equal(409, failure.Status);
        Assert.Equal(InvitationResponder.Stale, failure.Message);
    }

    [Fact]
    public async Task AFileTheGuardsRefuse_OrNotAnInvitation_Is422()
    {
        var sut = Create();
        PartText("garbage");
        var failure = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error;
        Assert.Equal(422, failure.Status);
        Assert.Equal(InvitationResponder.Unreadable, failure.Message);

        Part("google-reply");
        Assert.Equal(422, (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error.Status);
    }

    [Fact]
    public async Task AWriterRefusal_IsMapped()
    {
        var sut = Create();
        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.UidConflict, null, "/dav/x", 0));
        var conflict = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error;
        Assert.Equal(409, conflict.Status);
        Assert.Equal(InvitationResponder.CalendarConflict, conflict.Message);

        _writer.Setup(w => w.PutAsync(WebmailUid, Personal, It.IsAny<string>(), It.IsAny<string>(), None, false, null, RevisionCause.Webmail))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Busy, null, null, 0));
        var busy = (await sut.RespondAsync(_user, Conn, Request(InvitationAnswer.Accepted), None)).Error;
        Assert.Equal(502, busy.Status);
        Assert.Equal(InvitationResponder.CalendarBusy, busy.Message);
        _sender.VerifyNoOtherCalls();
    }
}
