using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Scotty.Microservice.Data.Preferences;
using weesky.Scotty.Microservice.Models;
using weesky.Scotty.Microservice.Models.Calendar;
using weesky.Scotty.Microservice.Models.Dav;
using weesky.Scotty.Microservice.Models.Mail;
using weesky.Scotty.Microservice.Repositories;
using weesky.Scotty.Microservice.Services;
using weesky.Scotty.Microservice.Services.Calendar.Invitations;
using weesky.Scotty.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Scotty.Microservice.Tests.Services.Calendar.Invitations;

public sealed class InvitationReplyApplierTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");
    private readonly User _user = new("alice@weesky.be") { WebmailUid = Guid.NewGuid() };
    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<ICalendarEventStore> _events = new();
    private readonly Mock<IDavCalendarWriter> _writer = new();

    private static string Fixture(string name) =>
        InvitationParserTests.Fixture(name).Replace("aaaa1111-bbbb-2222-cccc-3333dddd4444", "web-1111-2222");

    [Theory]
    [InlineData(DavWriteStatus.Busy, "calendar_busy")]
    [InlineData(DavWriteStatus.PreconditionFailed, "calendar_conflict")]
    public async Task AWriteTheCalendarDoesNotTake_Is200_NotApplied_WithItsCode(DavWriteStatus status, string code)
    {
        var stored = new StoredEventRef(Guid.NewGuid(), Guid.NewGuid(), "phone-name.ics", Fixture("webmail-invited"), "webmail", "abc123");
        _events.Setup(e => e.FindByUidAsync(_user.WebmailUid, "web-1111-2222", None)).ReturnsAsync([stored]);
        _messages.Setup(m => m.GetAttachmentAsync(_user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(() => Result.Success(new MailAttachmentContent
            {
                Content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Fixture("google-reply"))), ContentType = "text/calendar",
            }));
        string? written = null;
        _writer.Setup(w => w.PutAsync(_user.WebmailUid, stored.CalendarId, "phone-name.ics", It.IsAny<string>(), None, false, "\"abc123\"", RevisionCause.Webmail))
            .Callback<Guid, Guid, string, string, CancellationToken, bool, string?, RevisionCause>((_, _, _, ics, _, _, _, _) => written = ics)
            .ReturnsAsync(new DavWriteOutcome(status, null, null, 0));
        var sut = new InvitationReplyApplier(new InvitationPartLoader(_messages.Object),
            new InvitationReader(Mock.Of<IUserAddresses>(MockBehavior.Strict), _events.Object), _writer.Object,
            NullLogger<InvitationReplyApplier>.Instance);

        var result = await sut.ApplyAsync(_user, Conn, new ApplyReplyRequest { Folder = "INBOX", Uid = 7, Part = "2" }, None);

        Assert.True(result.IsSuccess);
        Assert.Equal((false, code), (result.Value.Applied, result.Value.ApplyError));
        Assert.Equal((ReplyStatus.Applicable, false), (result.Value.Invitation.Reply!.Status, result.Value.Invitation.Reply.Applied));
        Assert.Equal(PartStatRewriter.Rewrite(stored.IcsRaw, "marc.dupont@example.org", "DECLINED", "20260913T100000Z"), written);
        _writer.VerifyAll();
        _writer.VerifyNoOtherCalls();
    }
}
