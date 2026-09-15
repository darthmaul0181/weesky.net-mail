using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Calendar;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services.Calendar;
using weesky.Snoopy.Microservice.Services.Calendar.Delivery;
using weesky.Snoopy.Microservice.Services.Calendar.Invitations;
using weesky.Snoopy.Microservice.Tests.Fixtures;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using weesky.Snoopy.Microservice.Tests.Services.Calendar.Delivery;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class DeliveryControllerTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private const string Key = "k-k-k";
    private static readonly byte[] Hash = DeliveryKeys.Hash(Key);
    private readonly Mock<IDeliveryKeyProvider> _keys = new();
    private readonly Mock<IDeliveryKeyStore> _store = new();
    private readonly Mock<IWebmailUserStore> _users = new();
    private readonly Mock<IDeliveryReplyApplier> _applier = new();
    private readonly Mock<ILogger<DeliveryController>> _logger = new();
    private readonly MutableTimeProvider _clock = new();

    private DeliveryController Controller(
        string? key = Key, string? mailbox = "darth@weesky.be", string mail = "outlook-reply",
        DeliveryRefusals? refusals = null, Stream? body = null)
    {
        var http = new DefaultHttpContext();
        if (key is not null) http.Request.Headers[DeliveryController.KeyHeader] = key;
        if (mailbox is not null) http.Request.Headers[DeliveryController.MailboxHeader] = mailbox;
        http.Request.Body = body ?? DeliveryMailReaderTests.Mail(mail);
        return new DeliveryController(_keys.Object, _store.Object, _users.Object, _applier.Object,
            refusals ?? new DeliveryRefusals(_clock), _clock, _logger.Object)
        { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    private void KeyIs(bool enabled) => _keys.Setup(k => k.GetAsync(None)).ReturnsAsync(new DeliveryKeySnapshot(Hash, enabled));

    [Fact]
    public async Task Disabled_Is404Bare_EvenWithTheRightKey()
    {
        KeyIs(enabled: false);
        Assert.IsType<NotFoundResult>((await Controller().ApplyCalendarReply(None)).Result);
        _store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong")]
    public async Task AWrongOrMissingKey_Is404Bare(string? key)
    {
        KeyIs(enabled: true);
        Assert.IsType<NotFoundResult>((await Controller(key: key).ApplyCalendarReply(None)).Result);
        _store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task NoKeyStored_Is404Bare()
    {
        _keys.Setup(k => k.GetAsync(None)).ReturnsAsync((DeliveryKeySnapshot?)null);
        Assert.IsType<NotFoundResult>((await Controller().ApplyCalendarReply(None)).Result);
        _store.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    // The brief's own list named a plain valid address here ("darth@weesky.be"), which
    // TryNormalize accepts and would make the controller read the key — contradicting both this
    // test's name and its VerifyNoOtherCalls() below. A CRLF-injected header is what stays bad.
    [InlineData("darth@weesky.be\r\nX-Forged: 1")]
    public async Task ABadMailboxHeader_Is400_BeforeTheKeyIsEvenRead(string? mailbox)
    {
        var result = (await Controller(mailbox: mailbox).ApplyCalendarReply(None)).Result;
        Assert.IsType<BadRequestObjectResult>(result);
        _keys.VerifyNoOtherCalls();
        _logger.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AnUnknownMailbox_IsSaidSo_AfterTheCallIsRecorded()
    {
        KeyIs(enabled: true);
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync((WebmailAccount?)null);

        var response = (await Controller().ApplyCalendarReply(None)).Value!;

        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.UnknownMailbox, null, null), response);
        _store.Verify(s => s.RecordCallAsync(_clock.GetUtcNow().UtcDateTime, None), Times.Once);
        _applier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AReply_ReachesTheApplier_AsTheMailboxOwner()
    {
        KeyIs(enabled: true);
        var id = Guid.NewGuid();
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync(new WebmailAccount(id, Guid.NewGuid()));
        User? seen = null;
        _applier.Setup(a => a.ApplyAsync(It.IsAny<User>(), It.Is<string>(ics => ics.Contains("PARTSTAT=ACCEPTED")), None))
            .Callback<User, string, CancellationToken>((u, _, _) => seen = u)
            .ReturnsAsync(new DeliveryReplyResponse(DeliveryReplyOutcome.Applied, "u", null));

        var response = (await Controller().ApplyCalendarReply(None)).Value!;

        Assert.Equal(DeliveryReplyOutcome.Applied, response.Outcome);
        Assert.Equal(("darth@weesky.be", id), (seen!.Email, seen.WebmailUid));
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("forwarded-reply")]
    public async Task NotAReply_WhenNoPartIsRetained(string mail)
    {
        KeyIs(enabled: true);
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));

        var response = (await Controller(mail: mail).ApplyCalendarReply(None)).Value!;

        Assert.Equal(DeliveryReplyOutcome.NotAReply, response.Outcome);
        _applier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task TooLarge_IsNotApplicable_WithTheTooLargeDetail()
    {
        KeyIs(enabled: true);
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));
        var big = "From: a@b\r\nContent-Type: text/calendar; method=REPLY\r\n\r\n" + new string('x', IcsGuards.MaxIcsBytes + 1);
        var body = new MemoryStream(Encoding.ASCII.GetBytes(big));

        var response = (await Controller(body: body).ApplyCalendarReply(None)).Value!;

        Assert.Equal(new DeliveryReplyResponse(DeliveryReplyOutcome.NotApplicable, null, DeliveryController.TooLarge), response);
        _applier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AConflictOutcome_IsLoggedAtWarning()
    {
        KeyIs(enabled: true);
        _users.Setup(u => u.FindByEmailAsync("darth@weesky.be", None)).ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));
        _applier.Setup(a => a.ApplyAsync(It.IsAny<User>(), It.IsAny<string>(), None))
            .ReturnsAsync(new DeliveryReplyResponse(DeliveryReplyOutcome.Conflict, "u", "PreconditionFailed"));

        await Controller().ApplyCalendarReply(None);

        _logger.VerifyWarningLoggedContaining("Conflict");
    }

    // The prerequisite's activation procedure tells the operator to grep the service log for
    // exactly these two strings when the mail server's calls are refused — pinned here so neither
    // drifts under a later edit to the warning's wording.
    [Fact]
    public async Task ARefusalWhileDisabled_LogsDoorClosed()
    {
        KeyIs(enabled: false);

        await Controller().ApplyCalendarReply(None);

        _logger.VerifyWarningLoggedContaining("door closed");
    }

    [Fact]
    public async Task ARefusalWithTheWrongKey_LogsDoorOpenWrongKey()
    {
        KeyIs(enabled: true);

        await Controller(key: "wrong").ApplyCalendarReply(None);

        _logger.VerifyWarningLoggedContaining("open, wrong key");
    }

    [Fact]
    public async Task RefusedCalls_LogOnceAMinute()
    {
        KeyIs(enabled: true);
        // One shared DeliveryRefusals across both calls: the brief's own test built one per
        // Controller() call, so each refusal was "the first" on its own fresh instance and both
        // logged — the throttling this test means to exercise only exists when one instance sees
        // both refused calls, exactly as the real singleton registration does.
        var refusals = new DeliveryRefusals(_clock);
        await Controller(key: "wrong", refusals: refusals).ApplyCalendarReply(None);
        await Controller(key: "wrong", refusals: refusals).ApplyCalendarReply(None);
        // LoggerAssertions has no exact-count helper (only AtLeastOnce/Never), so this asserts the
        // count directly, as the brief's own fallback form does.
        _logger.Verify(l => l.Log(
                LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
