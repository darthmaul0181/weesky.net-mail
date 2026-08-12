using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Platform;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

/// <summary>
/// The From gate and the label it carries, on both sides of <see cref="IAliasDirectory.EnforcesOwnership"/>.
/// The rest of the factory — bodies, attachments, threading — is covered through MailSender/DraftSaver.
/// </summary>
public sealed class OutgoingMessageFactoryTests
{
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private static readonly MailAccountConnection Conn = TestConnections.Primary("mick@weesky.be", "pw");

    private readonly Mock<IAliasDirectory> _directory = new();
    private readonly Mock<IProfileReader> _profiles = new();
    private readonly Mock<ISendingIdentityStore> _identities = new();
    private readonly Mock<IOutgoingMailSanitizer> _sanitizer = new();
    private readonly Mock<IStagedAttachmentStore> _staged = new();
    private readonly User _user = new("mick@weesky.be") { WebmailUid = WebmailUid };

    private OutgoingMessageFactory CreateFactory(
        bool enforcesOwnership, IReadOnlyList<string>? aliases = null,
        IReadOnlyList<SendingIdentity>? stored = null, string? fullName = "Mick")
    {
        _directory.SetupGet(d => d.EnforcesOwnership).Returns(enforcesOwnership);
        _directory.Setup(d => d.GetAddressesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aliases ?? []);
        _profiles.Setup(p => p.GetDisplayNameAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullName);
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored ?? []);
        _sanitizer.Setup(s => s.Prepare(It.IsAny<string>())).Returns(new OutgoingBody("<div>hi</div>", "hi"));

        return new OutgoingMessageFactory(_directory.Object, _profiles.Object, _identities.Object,
            _sanitizer.Object, _staged.Object, NullLogger<OutgoingMessageFactory>.Instance);
    }

    private static SendMessageRequest Request(string? from = null) => new()
    {
        To = ["alice@example.com"], Subject = "Hi", HtmlBody = "<div>hi</div>", FromAddress = from
    };

    private static SendingIdentity Row(string address, string name) =>
        new() { UserId = WebmailUid, Address = address, DisplayName = name };

    // ── Strict: the alias list is the ownership rule ─────────────────────────

    [Fact]
    public async Task Strict_ALiveAliasIsAccepted_AndLabelledFromTheProfile()
    {
        var factory = CreateFactory(enforcesOwnership: true, aliases: ["michel@weesky.be"]);

        var result = await factory.CreateAsync(_user, Conn, Request("michel@weesky.be"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("michel@weesky.be", result.Value.From.Mailboxes.Single().Address);
        Assert.Equal("Mick", result.Value.From.Mailboxes.Single().Name);
    }

    [Fact]
    public async Task Strict_AnAddressOutsideTheAliasListIsRefused()
    {
        var factory = CreateFactory(enforcesOwnership: true, aliases: ["michel@weesky.be"]);

        var result = await factory.CreateAsync(_user, Conn, Request("intruder@evil.com"), CancellationToken.None);

        Assert.Equal(IOutgoingMessageFactory.ForbiddenFrom, result.Error);
    }

    /// <summary>A stored row owns nothing while the platform vouches for the alias list.</summary>
    [Fact]
    public async Task Strict_AStoredButNoLongerLiveAddressIsRefused()
    {
        var factory = CreateFactory(enforcesOwnership: true, stored: [Row("gone@weesky.be", "Ancien")]);

        var result = await factory.CreateAsync(_user, Conn, Request("gone@weesky.be"), CancellationToken.None);

        Assert.Equal(IOutgoingMessageFactory.ForbiddenFrom, result.Error);
    }

    // ── Free: exactly the connected rule, primary ∪ stored rows ──────────────

    [Fact]
    public async Task Free_AStoredIdentityIsAccepted_AndLabelledFromItsRow()
    {
        var factory = CreateFactory(enforcesOwnership: false, stored: [Row("me@elsewhere.test", "Me Elsewhere")]);

        var result = await factory.CreateAsync(_user, Conn, Request("me@elsewhere.test"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("me@elsewhere.test", result.Value.From.Mailboxes.Single().Address);
        Assert.Equal("Me Elsewhere", result.Value.From.Mailboxes.Single().Name);
    }

    /// <summary>An address nothing declares is still refused — the free path is not a free-for-all.</summary>
    [Fact]
    public async Task Free_AnUndeclaredAddressIsRefused()
    {
        var factory = CreateFactory(enforcesOwnership: false, aliases: ["michel@weesky.be"]);

        var result = await factory.CreateAsync(_user, Conn, Request("michel@weesky.be"), CancellationToken.None);

        Assert.Equal(IOutgoingMessageFactory.ForbiddenFrom, result.Error);
    }

    [Fact]
    public async Task Free_ThePrimaryAddressNeedsNoRow_AndTravelsBare()
    {
        var factory = CreateFactory(enforcesOwnership: false);

        var result = await factory.CreateAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var mailbox = result.Value.From.Mailboxes.Single();
        Assert.Equal("mick@weesky.be", mailbox.Address);
        Assert.Equal(string.Empty, mailbox.Name);
    }

    /// <summary>No platform to ask: neither port is consulted on the free path.</summary>
    [Fact]
    public async Task Free_ConsultsNeitherTheAliasListNorTheProfile()
    {
        var factory = CreateFactory(enforcesOwnership: false, stored: [Row("mick@weesky.be", "Mick")]);

        await factory.CreateAsync(_user, Conn, Request("mick@weesky.be"), CancellationToken.None);

        _directory.Verify(d => d.GetAddressesAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        _profiles.Verify(p => p.GetDisplayNameAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A connected account is judged by its own login and rows whatever the platform says.</summary>
    [Fact]
    public async Task Connected_IsUnaffectedByTheOwnershipFlag()
    {
        var id = Guid.NewGuid().ToString();
        var factory = CreateFactory(enforcesOwnership: true, aliases: ["michel@weesky.be"]);

        var result = await factory.CreateAsync(
            _user, TestConnections.Connected(id, "me@external.test", "pw2"),
            Request("michel@weesky.be"), CancellationToken.None);

        Assert.Equal(IOutgoingMessageFactory.ForbiddenFrom, result.Error);
    }
}
