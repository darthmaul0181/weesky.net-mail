using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services.Calendar.Invitations;

/// <summary>The responder over the real stores and the real CalDAV writer (InMemory base), the
/// mail side mocked: what the calendar actually holds after a sequence of answers.</summary>
public sealed class InvitationResponderStoreTests : IAsyncLifetime
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "pw");

    private readonly string database = Guid.NewGuid().ToString();
    private readonly Mock<IMailMessageRepository> _messages = new();
    private readonly Mock<IUserAddresses> _addresses = new();
    private readonly Mock<IMailSender> _sender = new();
    private readonly Mock<IRoleFolderLocator> _locator = new();
    private readonly Mock<IProfileReader> _profiles = new();
    private PreferencesTestDbContext context = null!;
    private InvitationResponder responder = null!;
    private User user = null!;

    public async Task InitializeAsync()
    {
        var (_, userId, _) = await CalendarStoreTestFactory.SeedAsync(database);
        user = new User("alice@weesky.be") { WebmailUid = userId };
        context = new PreferencesTestDbContext(database);
        var sync = new TestCalendarSyncStore(context);
        var events = new CalendarEventStore(context, sync, NullLogger<CalendarEventStore>.Instance);
        var writer = new DavCalendarWriter(events, sync, context, NullLogger<DavCalendarWriter>.Instance);
        _addresses.Setup(a => a.ForAccountAsync(user, Conn, None)).ReturnsAsync(["alice@weesky.be"]);
        _sender.Setup(s => s.SendBuiltAsync(user, Conn, It.IsAny<MimeMessage>(), None))
            .ReturnsAsync(Result.Success(new SendMessageResult(true)));
        _profiles.Setup(p => p.GetDisplayNameAsync(user, None)).ReturnsAsync("Alice");
        Part(InvitationParserTests.Fixture("google-request"));
        responder = new InvitationResponder(_messages.Object, new InvitationReader(_addresses.Object, events),
            new CalendarStore(context, sync), writer, _sender.Object, _locator.Object, _profiles.Object,
            NullLogger<InvitationResponder>.Instance);
    }

    public Task DisposeAsync()
    {
        context.Dispose();
        return Task.CompletedTask;
    }

    private void Part(string ics) =>
        _messages.Setup(m => m.GetAttachmentAsync(user, Conn, "INBOX", 7u, "2", None))
            .ReturnsAsync(() => Result.Success(new MailAttachmentContent
            {
                Content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(ics)), ContentType = "text/calendar",
            }));

    private static RespondInvitationRequest Request(InvitationAnswer answer) => new()
    {
        Folder = "INBOX", Uid = 7, Part = "2", Answer = answer, Language = "fr", TimeZone = "Europe/Brussels",
    };

    [Fact]
    public async Task ChangingOnesMind_RewritesTheStoredPartStat_AndTheBlockSaysSo()
    {
        var accepted = await responder.RespondAsync(user, Conn, Request(InvitationAnswer.Accepted), None);
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Message : "");
        Assert.Equal(InvitationPresence.Current, accepted.Value.Invitation.InCalendar);
        Assert.Equal("ACCEPTED", accepted.Value.Invitation.SavedPartStat);

        var tentative = await responder.RespondAsync(user, Conn, Request(InvitationAnswer.Tentative), None);

        Assert.True(tentative.IsSuccess, tentative.IsFailure ? tentative.Error.Message : "");
        var row = await context.CalendarEvents.AsNoTracking().SingleAsync();
        Assert.Equal("TENTATIVE", InvitationParser.PartStatOf(row.IcsRaw, "alice@weesky.be"));
        Assert.Equal("TENTATIVE", tentative.Value.Invitation.SavedPartStat);
        // The grid draws « provisoire » from this, not from the organizer's STATUS.
        var events = new CalendarEventStore(context, new TestCalendarSyncStore(context), NullLogger<CalendarEventStore>.Instance);
        var mine = await events.OwnPartStatsAsync(user.WebmailUid, [row.Id], ["alice@weesky.be"], None);
        Assert.Equal("TENTATIVE", mine[row.Id]);
    }
}
