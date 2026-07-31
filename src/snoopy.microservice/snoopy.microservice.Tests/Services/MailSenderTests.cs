using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailSenderTests
{
    private static readonly MailAccountConnection Conn = TestConnections.Primary("mick@weesky.be", "pw");
    private static readonly string ConnectedId = Guid.NewGuid().ToString();
    private static readonly MailAccountConnection Connected =
        TestConnections.Connected(ConnectedId, "mick@external.test", "pw2");

    private readonly Mock<IUsersRepository> _users = new();
    private readonly Mock<IAliasesRepository> _aliases = new();
    private readonly Mock<ISendingIdentityStore> _identities = new();
    private readonly Mock<IOutgoingMailSanitizer> _sanitizer = new();
    private readonly Mock<IStagedAttachmentStore> _staged = new();
    private readonly Mock<ISmtpConnectionFactory> _smtpFactory = new();
    private readonly Mock<ISmtpSession> _smtp = new();
    private readonly Mock<IMailFolderRepository> _folders = new();
    private readonly Mock<IFolderRoleStore> _roles = new();
    private readonly Mock<IMailMessageRepository> _messages = new();
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private readonly User _user = new("mick@weesky.be") { WebmailUid = WebmailUid };

    private MailSender CreateSender()
    {
        _users.Setup(u => u.FindByEmailAsync("mick@weesky.be"))
            .ReturnsAsync(new User("mick@weesky.be") { FullName = "Mick" });
        _sanitizer.Setup(s => s.Prepare(It.IsAny<string>()))
            .Returns(new OutgoingBody("<div>hi</div>", "hi"));
        _smtpFactory.Setup(f => f.OpenAsync(Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(_smtp.Object));
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // A tree whose "Sent" folder carries the server flag: the resolver finds the role.
        var sent = new MailFolderNode { Name = "Sent", Path = "Sent", AttributeRole = "sent", Selectable = true };
        _folders.Setup(f => f.GetTreeAsync(_user, Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([sent]));
        _roles.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FolderRoleOverride>());
        _messages.Setup(m => m.AppendAsync(_user, Conn, "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _aliases.Setup(a => a.GetAliasesAsync(It.IsAny<User>()))
            .ReturnsAsync([new Alias { Name = "michel", Domain = "weesky.be" }]);
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Every setup above is the happy path; a test that wants another one overrides it on the
        // shared mocks *after* CreateSender(), since a later Setup replaces an earlier one.
        // The real factory, not a mock: these tests assert on the built message's wire form.
        var factory = new OutgoingMessageFactory(_users.Object, _aliases.Object, _identities.Object,
            _sanitizer.Object, _staged.Object, NullLogger<OutgoingMessageFactory>.Instance);
        return new MailSender(factory, _staged.Object, _smtpFactory.Object, _folders.Object,
            _roles.Object, _messages.Object, NullLogger<MailSender>.Instance);
    }

    private static SendMessageRequest Request() => new()
    {
        To = ["alice@example.com"], Bcc = ["hidden@example.com"], Subject = "Hi", HtmlBody = "<div>hi</div>"
    };

    [Fact]
    public async Task SendAsync_BuildsFromBodyAndBccAndAppendsSeen()
    {
        var sender = CreateSender();
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AppendedToSent);
        Assert.Equal("Mick", ((MailboxAddress)sent!.From[0]).Name);
        Assert.Equal("mick@weesky.be", ((MailboxAddress)sent.From[0]).Address);
        Assert.Equal("hidden@example.com", ((MailboxAddress)sent.Bcc[0]).Address);
        Assert.Contains("hi", sent.HtmlBody);
        Assert.Equal("hi", sent.TextBody!.Trim());
        _messages.Verify(m => m.AppendAsync(_user, Conn, "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()), Times.Once);
        _identities.Verify(i => i.GetAsync(WebmailUid, AccountScope.Primary, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_FailsOnAnUnknownAttachmentBeforeSending()
    {
        var id = Guid.NewGuid();
        _staged.Setup(s => s.Open(It.IsAny<string>(), id))
            .Returns(Result.Failure<StagedAttachment>("unknown_attachment"));

        var result = await CreateSender().SendAsync(
            _user, Conn, Request() with { AttachmentIds = [id] }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.UnknownAttachment, result.Error);
        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Never);
        _smtp.Verify(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_FailsWhenSmtpCannotBeOpened()
    {
        var sender = CreateSender();
        _smtpFactory.Setup(f => f.OpenAsync(Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ISmtpSession>("Unable to connect to the mail server"));

        var result = await sender.SendAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Unable to connect to the mail server", result.Error);
        _smtp.Verify(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_KeepsStagedFilesWhenSmtpRefuses()
    {
        var id = Guid.NewGuid();
        var path = Path.GetTempFileName();
        try
        {
            _staged.Setup(s => s.Open(It.IsAny<string>(), id)).Returns(Result.Success(
                new StagedAttachment(new StagedAttachmentInfo(id, "a.txt", 4, "text/plain"), path)));
            var sender = CreateSender();
            _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure("The mail server refused the message"));

            var result = await sender.SendAsync(
                _user, Conn, Request() with { AttachmentIds = [id] }, CancellationToken.None);

            Assert.True(result.IsFailure);
            _staged.Verify(s => s.Delete(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
            _messages.Verify(m => m.AppendAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
                It.IsAny<MimeMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SendAsync_ReportsAFailedAppendWithoutFailingTheSend()
    {
        var sender = CreateSender();
        _messages.Setup(m => m.AppendAsync(_user, Conn, "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to file the message"));

        var result = await sender.SendAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AppendedToSent);
    }

    [Fact]
    public async Task SendAsync_ReportsNoSentRoleWithoutFailingTheSend()
    {
        var sender = CreateSender();
        _folders.Setup(f => f.GetTreeAsync(_user, Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(
                [new MailFolderNode { Name = "Stuff", Path = "Stuff", Selectable = true }]));

        var result = await sender.SendAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AppendedToSent);
    }

    [Fact]
    public async Task SendAsync_ReportsAThrowingRoleLookupWithoutFailingTheSend()
    {
        var sender = CreateSender();
        _roles.Setup(r => r.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("preferences database is down"));

        var result = await sender.SendAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AppendedToSent);
    }

    [Fact]
    public async Task SendAsync_HonoursTheDeclaredAttachmentContentType()
    {
        var id = Guid.NewGuid();
        var path = Path.GetTempFileName();
        try
        {
            // Extension-less name: only the declared content type can carry the MIME type.
            _staged.Setup(s => s.Open(It.IsAny<string>(), id)).Returns(Result.Success(
                new StagedAttachment(new StagedAttachmentInfo(id, "report", 4, "application/pdf"), path)));
            var sender = CreateSender();
            MimeMessage? sent = null;
            _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
                .ReturnsAsync(Result.Success());

            await sender.SendAsync(_user, Conn, Request() with { AttachmentIds = [id] }, CancellationToken.None);

            var part = Assert.IsAssignableFrom<MimePart>(sent!.Attachments.Single());
            Assert.Equal("application/pdf", part.ContentType.MimeType);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SendAsync_MapsAVanishedStagedFileToUnknownAttachment()
    {
        var id = Guid.NewGuid();
        // Open resolves, but the file is gone by the time BuildMessage reads it (TTL/concurrent DELETE).
        _staged.Setup(s => s.Open(It.IsAny<string>(), id)).Returns(Result.Success(
            new StagedAttachment(new StagedAttachmentInfo(id, "a.txt", 4, "text/plain"),
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))));
        var sender = CreateSender();

        var result = await sender.SendAsync(
            _user, Conn, Request() with { AttachmentIds = [id] }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.UnknownAttachment, result.Error);
        _smtp.Verify(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_PurgesStagedFilesAfterASuccessfulSend()
    {
        var id = Guid.NewGuid();
        var path = Path.GetTempFileName();
        try
        {
            _staged.Setup(s => s.Open(It.IsAny<string>(), id)).Returns(Result.Success(
                new StagedAttachment(new StagedAttachmentInfo(id, "a.txt", 4, "text/plain"), path)));

            var result = await CreateSender().SendAsync(
                _user, Conn, Request() with { AttachmentIds = [id] }, CancellationToken.None);

            Assert.True(result.IsSuccess);
            // The purge must hit the user-scoped namespace the upload staged into.
            _staged.Verify(s => s.Delete(Conn.StagedScope(_user), id), Times.Once);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SendAsync_FromAlias_UsesTheAliasWithItsStoredLabel()
    {
        var sender = CreateSender();
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "michel@weesky.be", DisplayName = "Michel Dubois" }]);
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Conn,
            Request() with { FromAddress = " Michel@Weesky.BE " }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var from = Assert.IsType<MailboxAddress>(sent!.From[0]);
        Assert.Equal("michel@weesky.be", from.Address);
        Assert.Equal("Michel Dubois", from.Name);
    }

    [Fact]
    public async Task SendAsync_FromAliasWithoutARow_FallsBackToTheFullName()
    {
        var sender = CreateSender();
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Conn,
            Request() with { FromAddress = "michel@weesky.be" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Mick", Assert.IsType<MailboxAddress>(sent!.From[0]).Name);
    }

    [Fact]
    public async Task SendAsync_ForeignFrom_FailsBeforeAnySmtpConnection()
    {
        var sender = CreateSender();

        var result = await sender.SendAsync(_user, Conn,
            Request() with { FromAddress = "intruder@evil.com" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.ForbiddenFrom, result.Error);
        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_FromAStaleIdentity_IsRefused()
    {
        var sender = CreateSender();
        // The row survives in the identity table but the alias was deleted: the alias list decides.
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "gone@weesky.be", DisplayName = "Ancien" }]);

        var result = await sender.SendAsync(_user, Conn,
            Request() with { FromAddress = "gone@weesky.be" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.ForbiddenFrom, result.Error);
        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_AThrowingIdentityStore_StillSendsWithTheFullNameLabel()
    {
        var sender = CreateSender();
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("preferences database is down"));
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Conn,
            Request() with { FromAddress = "michel@weesky.be" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var from = Assert.IsType<MailboxAddress>(sent!.From[0]);
        Assert.Equal("michel@weesky.be", from.Address);
        Assert.Equal("Mick", from.Name);
    }

    [Fact]
    public async Task SendAsync_TheCallerCancelling_PropagatesInsteadOfDegrading()
    {
        var sender = CreateSender();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(_user, Conn, Request(), cancelled.Token));

        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_AnIdentityStoreTimingOutAsCancelled_StillSends()
    {
        var sender = CreateSender();
        // The caller never cancelled: a preferences-layer timeout is an outage, so the send degrades.
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Mick", ((MailboxAddress)sent!.From[0]).Name);
    }

    [Fact]
    public async Task SendAsync_AsThePrimaryWithAStoredPrimaryRow_UsesTheAccountFullName()
    {
        var sender = CreateSender();
        // A stored row for the primary must never override the label — the FullName is the only source.
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "mick@weesky.be", DisplayName = "Le Boss" }]);
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Conn,
            Request() with { FromAddress = "mick@weesky.be" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var from = Assert.IsType<MailboxAddress>(sent!.From[0]);
        Assert.Equal("mick@weesky.be", from.Address);
        Assert.Equal("Mick", from.Name);
    }

    [Fact]
    public async Task SendAsync_NoFromAddress_KeepsThePrimaryBehaviour()
    {
        var sender = CreateSender();
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        Assert.True((await sender.SendAsync(_user, Conn, Request(), CancellationToken.None)).IsSuccess);
        Assert.Equal("mick@weesky.be", Assert.IsType<MailboxAddress>(sent!.From[0]).Address);
    }

    [Fact]
    public async Task SendAsync_SetsTheThreadingHeaders()
    {
        MimeMessage? sent = null;
        var sender = CreateSender();
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var request = Request() with { InReplyTo = "parent@id", References = ["root@id", "parent@id"] };
        var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("parent@id", sent!.InReplyTo);
        Assert.Equal(new[] { "root@id", "parent@id" }, sent.References);
        Assert.EndsWith("@weesky.be", sent.MessageId);
    }

    [Fact]
    public async Task SendAsync_AReferenceCarryingCrlf_CannotInjectAHeader()
    {
        MimeMessage? sent = null;
        var sender = CreateSender();
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var request = Request() with { References = ["a@b\r\nBcc: evil@example.com"] };
        var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "a@b" }, sent!.References);
        // Request() already carries one legitimate Bcc: a second one would be the injected line.
        Assert.Single(sent.Headers, h => h.Field.Equals("Bcc", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("evil@example.com", sent.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_MalformedThreadingIds_AreNormalisedNotRejected()
    {
        MimeMessage? sent = null;
        var sender = CreateSender();
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var request = Request() with { InReplyTo = "not a msgid!!", References = ["", "garbage", "root@id"] };
        var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // A bad id never costs the ones after it: the valid tail of the chain still ships.
        Assert.Contains("root@id", sent!.References);
        Assert.DoesNotContain("", sent.References);
        // MimeKit's parser is the gate, and it normalises junk rather than rejecting it — what
        // matters is that only a parsed msg-id ever reaches the wire, and that the send survives.
        Assert.Equal("notamsgid!!", sent.InReplyTo);
    }

    [Fact]
    public async Task SendAsync_WithoutThreading_SendsAFreshMessage()
    {
        MimeMessage? sent = null;
        var sender = CreateSender();
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(sent!.InReplyTo);
        Assert.Empty(sent.References);
        Assert.EndsWith("@weesky.be", sent.MessageId);
    }

    // The composer loads a staged image as an <img> subresource, which cannot carry the
    // X-Account-Id header, so on a connected account it names the account in the query string
    // instead. Both spellings must inline identically, or every inline image of a connected
    // account would be dropped from the sent message.
    [Theory]
    [InlineData("")]
    [InlineData("?account=9c2f6a5d-4b3e-4a1c-8d7f-0e6b5a4c3d2e")]
    public async Task SendAsync_PacksAReferencedInlinePartAsALinkedResource(string accountQuery)
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        try
        {
            MimeMessage? sent = null;
            var sender = CreateSender();
            var info = new StagedAttachmentInfo(id, "logo.png", 3, "image/png", "logo@mail");
            _staged.Setup(s => s.Open(It.IsAny<string>(), id))
                .Returns(Result.Success(new StagedAttachment(info, path)));
            // Pass-through: what the sanitizer received is what the rewrite must have produced.
            _sanitizer.Setup(s => s.Prepare(It.IsAny<string>()))
                .Returns<string>(html => new OutgoingBody(html, "hi"));
            _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
                .ReturnsAsync(Result.Success());

            var request = Request() with
            {
                HtmlBody = $"<p>Hi</p><img src=\"/api/Mail/Attachments/{id}/content{accountQuery}\">",
                AttachmentIds = [id],
            };
            var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

            Assert.True(result.IsSuccess);
            var resource = sent!.BodyParts.OfType<MimePart>().Single(p => p.ContentId == "logo@mail");
            Assert.Equal("image/png", resource.ContentType.MimeType);
            Assert.Contains("cid:logo@mail", sent.HtmlBody);
            Assert.DoesNotContain("/api/Mail/Attachments/", sent.HtmlBody);
            // Packed as a related resource, not an attachment.
            Assert.DoesNotContain(sent.Attachments, a => a is MimePart mp && mp.ContentId == "logo@mail");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SendAsync_PacksAnInlinePartWhoseContentIdCarriesAnAmpersand()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        try
        {
            MimeMessage? sent = null;
            var sender = CreateSender();
            // '&' is legal in a Content-ID and comes off the inbound part untouched.
            var info = new StagedAttachmentInfo(id, "logo.png", 3, "image/png", "a&b@x");
            _staged.Setup(s => s.Open(It.IsAny<string>(), id))
                .Returns(Result.Success(new StagedAttachment(info, path)));
            // The real sanitizer, because the bug is in its re-serialisation: the formatter escapes
            // the '&' in the src, so a check against the raw HTML no longer finds the reference.
            _sanitizer.Setup(s => s.Prepare(It.IsAny<string>()))
                .Returns<string>(html => new OutgoingMailSanitizer().Prepare(html));
            _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
                .ReturnsAsync(Result.Success());

            var request = Request() with
            {
                HtmlBody = $"<p>Hi</p><img src=\"/api/Mail/Attachments/{id}/content\">",
                AttachmentIds = [id],
            };
            var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Contains(sent!.BodyParts.OfType<MimePart>(), p => p.ContentId == "a&b@x");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SendAsync_SkipsAnInlinePartTheBodyNoLongerReferences()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(path, new byte[] { 1 });
        try
        {
            MimeMessage? sent = null;
            var sender = CreateSender();
            var info = new StagedAttachmentInfo(id, "logo.png", 1, "image/png", "logo@mail");
            _staged.Setup(s => s.Open(It.IsAny<string>(), id))
                .Returns(Result.Success(new StagedAttachment(info, path)));
            _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
                .ReturnsAsync(Result.Success());

            // The user deleted the image in the editor: the id is still staged, the body has no URL.
            var request = Request() with { HtmlBody = "<p>no image left</p>", AttachmentIds = [id] };
            var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain(sent!.BodyParts.OfType<MimePart>(), p => p.ContentId == "logo@mail");
            _staged.Verify(s => s.Delete(It.IsAny<string>(), id), Times.Once); // still purged after send
        }
        finally { File.Delete(path); }
    }

    // ── Plain text ──────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WithATextBodySendsPlainTextAlone()
    {
        var sender = CreateSender();
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var request = Request() with { HtmlBody = string.Empty, TextBody = "hello\nthere" };
        var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // CRLF because that is the wire's line ending, written by MimeKit as the part is encoded.
        Assert.Equal("hello\r\nthere", sent!.TextBody);
        // No HTML twin: the whole point of the mode is that the recipient gets text.
        Assert.Null(sent.HtmlBody);
        _sanitizer.Verify(s => s.Prepare(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_InTextModePacksAnInlinePartAsAnOrdinaryAttachment()
    {
        var id = Guid.NewGuid();
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
        try
        {
            MimeMessage? sent = null;
            var sender = CreateSender();
            // Staged as an inline part by the quote preparer: it carries a Content-ID.
            var info = new StagedAttachmentInfo(id, "logo.png", 3, "image/png", "logo@mail");
            _staged.Setup(s => s.Open(It.IsAny<string>(), id))
                .Returns(Result.Success(new StagedAttachment(info, path)));
            _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
                .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
                .ReturnsAsync(Result.Success());

            var request = Request() with
            {
                HtmlBody = string.Empty, TextBody = "see the logo", AttachmentIds = [id],
            };
            var result = await sender.SendAsync(_user, Conn, request, CancellationToken.None);

            Assert.True(result.IsSuccess);
            // A text body references no cid, so it travels as a file rather than being dropped.
            var attachment = Assert.IsType<MimePart>(Assert.Single(sent!.Attachments));
            Assert.Equal("logo.png", attachment.FileName);
        }
        finally { File.Delete(path); }
    }

    // ── Connected accounts ──────────────────────────────────────────────

    private MailSender CreateSenderForConnectedAccount()
    {
        var sender = CreateSender();
        _smtpFactory.Setup(f => f.OpenAsync(Connected, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(_smtp.Object));
        _folders.Setup(f => f.GetTreeAsync(_user, Connected, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([]));
        return sender;
    }

    // The home server's alias list says nothing about a mailbox living on another server.
    [Fact]
    public async Task Send_RefusesAFromForeignToTheConnectedAccount()
    {
        var sender = CreateSenderForConnectedAccount();

        var result = await sender.SendAsync(_user, Connected,
            Request() with { FromAddress = "michel@weesky.be" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.ForbiddenFrom, result.Error);
        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Send_AcceptsAStoredIdentityOfTheConnectedAccount()
    {
        var sender = CreateSenderForConnectedAccount();
        _identities.Setup(i => i.GetAsync(WebmailUid, ConnectedId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "sales@external.test", DisplayName = "Sales" }]);
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Connected,
            Request() with { FromAddress = " Sales@External.test " }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var from = Assert.IsType<MailboxAddress>(sent!.From[0]);
        Assert.Equal("sales@external.test", from.Address);
        Assert.Equal("Sales", from.Name);
    }

    // No From, no stored row: the account's own login address, and no borrowed label — the
    // main account's FullName has nothing to do with this mailbox.
    [Fact]
    public async Task Send_FromAConnectedAccountDefaultsToItsOwnAddressWithoutALabel()
    {
        var sender = CreateSenderForConnectedAccount();
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, Connected, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var from = Assert.IsType<MailboxAddress>(sent!.From[0]);
        Assert.Equal("mick@external.test", from.Address);
        Assert.Equal(string.Empty, from.Name);
        _identities.Verify(i => i.GetAsync(WebmailUid, ConnectedId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // Whatever From was chosen, the session is the connected account's own.
    [Fact]
    public async Task Send_AuthenticatesOnTheConnectedAccountsOwnServer()
    {
        var sender = CreateSenderForConnectedAccount();

        var result = await sender.SendAsync(_user, Connected, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _smtpFactory.Verify(f => f.OpenAsync(Connected, It.IsAny<CancellationToken>()), Times.Once);
        _staged.Verify(s => s.Delete(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task Send_ScopesTheSentRoleOverridesToTheConnectedAccount()
    {
        var sender = CreateSenderForConnectedAccount();

        await sender.SendAsync(_user, Connected, Request(), CancellationToken.None);

        _roles.Verify(r => r.GetAsync(WebmailUid, ConnectedId, It.IsAny<CancellationToken>()), Times.Once);
        _roles.Verify(r => r.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()), Times.Never);
    }
}
