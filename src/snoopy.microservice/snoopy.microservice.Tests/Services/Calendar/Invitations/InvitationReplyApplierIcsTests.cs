using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Dav;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

/// <summary>The text entry of the applier — what the delivery door calls — over mocks, and its
/// projection onto the door's outcomes.</summary>
public sealed class InvitationReplyApplierIcsTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private readonly User _user = new("alice@weesky.be") { WebmailUid = Guid.NewGuid() };
    private readonly Mock<ICalendarEventStore> _events = new();
    private readonly Mock<IDavCalendarWriter> _writer = new();

    private static string Fixture(string name) =>
        InvitationParserTests.Fixture(name).Replace("aaaa1111-bbbb-2222-cccc-3333dddd4444", "web-1111-2222");

    private InvitationReplyApplier Sut() => new(new InvitationPartLoader(Mock.Of<IMailMessageRepository>()),
        new InvitationReader(Mock.Of<IUserAddresses>(MockBehavior.Strict), _events.Object), _writer.Object,
        NullLogger<InvitationReplyApplier>.Instance);

    private StoredEventRef Invited(string owner = "webmail") =>
        new(Guid.NewGuid(), Guid.NewGuid(), "phone-name.ics", Fixture("webmail-invited"), owner, "abc123");

    [Fact]
    public async Task ARequest_IsNotAReply()
    {
        var outcome = await Sut().ApplyIcsAsync(_user, Fixture("google-request"), RevisionCause.Delivery, None);
        Assert.Equal((InvitationReplyApplier.NotAReply, (ParsedInvitation?)null), (outcome.Refusal, outcome.Parsed));
        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-request"), None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.NotAReply, null, null), projected);
    }

    /// <summary>A REPLY with no ATTENDEE line at all: InvitationReader.ResolveReplyAsync leaves
    /// Reply null, ApplyIcsAsync refuses NotApplicable, and the delivery projection must still say
    /// notAReply — the spec's outcome table (§ La porte d'entrée), not a lying notApplicable.</summary>
    [Fact]
    public async Task AReplyWithNoAttendeeLine_ProjectsNotAReply()
    {
        var withoutAttendee = string.Join("\r\n", Fixture("google-reply").Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith("ATTENDEE", StringComparison.OrdinalIgnoreCase)));

        var outcome = await Sut().ApplyIcsAsync(_user, withoutAttendee, RevisionCause.Delivery, None);
        Assert.Equal((InvitationReplyApplier.NotApplicable, (InvitationReply?)null), (outcome.Refusal, outcome.Context?.Reply));

        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, withoutAttendee, None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.NotAReply, null, null), projected);
    }

    /// <summary>The reader decodes a mailto then trims, the rewriter trims then decodes: a stored
    /// line spelt mailto:%20marc… is the guest to one and a stranger to the other, so the rewrite
    /// finds no line to change — the one refusal that is not a ReplyStatus.</summary>
    [Fact]
    public async Task AGuestLineTheRewriterCannotFind_IsNotApplicable_AttendeeLineMissing()
    {
        var stored = Invited() with { IcsRaw = Fixture("webmail-invited").Replace("mailto:marc.dupont@example.org", "mailto:%20marc.dupont@example.org") };
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([stored]);

        var outcome = await Sut().ApplyIcsAsync(_user, Fixture("google-reply"), RevisionCause.Delivery, None);
        Assert.Equal((InvitationReplyApplier.AttendeeLineMissing, ReplyStatus.Applicable), (outcome.Refusal, outcome.Context!.Reply!.Status));

        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-reply"), None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.NotApplicable, "web-1111-2222", "attendee_line_missing"), projected);
        _writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownUid_IsNotApplicable_WithTheStatusAsDetail()
    {
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([]);
        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-reply"), None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.NotApplicable, "web-1111-2222", "UnknownUid"), projected);
    }

    [Fact]
    public async Task AnApplicableReply_IsWrittenUnderTheCauseGiven_AndAppliedIsDistinctFromAlreadyApplied()
    {
        var stored = Invited();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([stored]);
        _writer.Setup(w => w.PutAsync(_user.WebmailUid, stored.CalendarId, "phone-name.ics", It.IsAny<string>(), None, false, "\"abc123\"", RevisionCause.Delivery))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.Replaced, null, null, 0));

        var outcome = await Sut().ApplyIcsAsync(_user, Fixture("google-reply"), RevisionCause.Delivery, None);

        Assert.Equal((true, false, (string?)null), (outcome.Applied, outcome.AlreadyApplied, outcome.Refusal));
        _writer.VerifyAll();

        // The stored file now holds the answer: the same reply is "already applied", nothing is written.
        var answered = stored with { IcsRaw = PartStatRewriter.Rewrite(stored.IcsRaw, "marc.dupont@example.org", "DECLINED", "20260913T100000Z")! };
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([answered]);
        var again = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-reply"), None);
        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.AlreadyApplied, "web-1111-2222", null), again);
        _writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AWriteTheCalendarDoesNotTake_IsAConflict_WithTheStatusAsDetail()
    {
        var stored = Invited();
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([stored]);
        _writer.Setup(w => w.PutAsync(_user.WebmailUid, stored.CalendarId, "phone-name.ics", It.IsAny<string>(), None, false, "\"abc123\"", RevisionCause.Delivery))
            .ReturnsAsync(new DavWriteOutcome(DavWriteStatus.PreconditionFailed, null, null, 0));

        var projected = await ((IDeliveryReplyApplier)Sut()).ApplyAsync(_user, Fixture("google-reply"), None);

        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.Conflict, "web-1111-2222", "PreconditionFailed"), projected);
    }
}
