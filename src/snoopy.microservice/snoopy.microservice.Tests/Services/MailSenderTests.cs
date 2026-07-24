using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Services;

public sealed class MailSenderTests
{
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
    private readonly User _user = new("mick@weesky.be");

    private MailSender CreateSender()
    {
        _users.Setup(u => u.FindByEmailAsync("mick@weesky.be"))
            .ReturnsAsync(new User("mick@weesky.be") { FullName = "Mick" });
        _sanitizer.Setup(s => s.Prepare(It.IsAny<string>()))
            .Returns(new OutgoingBody("<div>hi</div>", "hi"));
        _smtpFactory.Setup(f => f.OpenAsync("mick@weesky.be", "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(_smtp.Object));
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // A tree whose "Sent" folder carries the server flag: the resolver finds the role.
        var sent = new MailFolderNode { Name = "Sent", Path = "Sent", AttributeRole = "sent", Selectable = true };
        _folders.Setup(f => f.GetTreeAsync(_user, "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([sent]));
        _roles.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FolderRoleOverride>());
        _messages.Setup(m => m.AppendAsync(_user, "pw", "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _aliases.Setup(a => a.GetAliasesAsync(It.IsAny<User>()))
            .ReturnsAsync([new Alias { Name = "michel", Domain = "weesky.be" }]);
        _identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Every setup above is the happy path; a test that wants another one overrides it on the
        // shared mocks *after* CreateSender(), since a later Setup replaces an earlier one.
        return new MailSender(_users.Object, _aliases.Object, _identities.Object, _sanitizer.Object,
            _staged.Object, _smtpFactory.Object, _folders.Object, _roles.Object, _messages.Object,
            NullLogger<MailSender>.Instance);
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

        var result = await sender.SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AppendedToSent);
        Assert.Equal("Mick", ((MailboxAddress)sent!.From[0]).Name);
        Assert.Equal("mick@weesky.be", ((MailboxAddress)sent.From[0]).Address);
        Assert.Equal("hidden@example.com", ((MailboxAddress)sent.Bcc[0]).Address);
        Assert.Contains("hi", sent.HtmlBody);
        Assert.Equal("hi", sent.TextBody.Trim());
        _messages.Verify(m => m.AppendAsync(_user, "pw", "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_FailsOnAnUnknownAttachmentBeforeSending()
    {
        var id = Guid.NewGuid();
        _staged.Setup(s => s.Open(It.IsAny<string>(), id))
            .Returns(Result.Failure<StagedAttachment>("unknown_attachment"));

        var result = await CreateSender().SendAsync(
            _user, "pw", Request() with { AttachmentIds = [id] }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.UnknownAttachment, result.Error);
        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _smtp.Verify(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendAsync_FailsWhenSmtpCannotBeOpened()
    {
        var sender = CreateSender();
        _smtpFactory.Setup(f => f.OpenAsync("mick@weesky.be", "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<ISmtpSession>("Unable to connect to the mail server"));

        var result = await sender.SendAsync(_user, "pw", Request(), CancellationToken.None);

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
                _user, "pw", Request() with { AttachmentIds = [id] }, CancellationToken.None);

            Assert.True(result.IsFailure);
            _staged.Verify(s => s.Delete(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
            _messages.Verify(m => m.AppendAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<MimeMessage>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SendAsync_ReportsAFailedAppendWithoutFailingTheSend()
    {
        var sender = CreateSender();
        _messages.Setup(m => m.AppendAsync(_user, "pw", "Sent", It.IsAny<MimeMessage>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure("Unable to file the message"));

        var result = await sender.SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AppendedToSent);
    }

    [Fact]
    public async Task SendAsync_ReportsNoSentRoleWithoutFailingTheSend()
    {
        var sender = CreateSender();
        _folders.Setup(f => f.GetTreeAsync(_user, "pw", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(
                [new MailFolderNode { Name = "Stuff", Path = "Stuff", Selectable = true }]));

        var result = await sender.SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AppendedToSent);
    }

    [Fact]
    public async Task SendAsync_ReportsAThrowingRoleLookupWithoutFailingTheSend()
    {
        var sender = CreateSender();
        _roles.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("preferences database is down"));

        var result = await sender.SendAsync(_user, "pw", Request(), CancellationToken.None);

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

            await sender.SendAsync(_user, "pw", Request() with { AttachmentIds = [id] }, CancellationToken.None);

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
            _user, "pw", Request() with { AttachmentIds = [id] }, CancellationToken.None);

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
                _user, "pw", Request() with { AttachmentIds = [id] }, CancellationToken.None);

            Assert.True(result.IsSuccess);
            _staged.Verify(s => s.Delete(It.IsAny<string>(), id), Times.Once);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SendAsync_FromAlias_UsesTheAliasWithItsStoredLabel()
    {
        var sender = CreateSender();
        _identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "michel@weesky.be", DisplayName = "Michel Dubois" }]);
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, "pw",
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

        var result = await sender.SendAsync(_user, "pw",
            Request() with { FromAddress = "michel@weesky.be" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Mick", Assert.IsType<MailboxAddress>(sent!.From[0]).Name);
    }

    [Fact]
    public async Task SendAsync_ForeignFrom_FailsBeforeAnySmtpConnection()
    {
        var sender = CreateSender();

        var result = await sender.SendAsync(_user, "pw",
            Request() with { FromAddress = "intruder@evil.com" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.ForbiddenFrom, result.Error);
        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_FromAStaleIdentity_IsRefused()
    {
        var sender = CreateSender();
        // The row survives in the identity table but the alias was deleted: the alias list decides.
        _identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SendingIdentity { Address = "gone@weesky.be", DisplayName = "Ancien" }]);

        var result = await sender.SendAsync(_user, "pw",
            Request() with { FromAddress = "gone@weesky.be" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IMailSender.ForbiddenFrom, result.Error);
        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_AThrowingIdentityStore_StillSendsWithTheFullNameLabel()
    {
        var sender = CreateSender();
        _identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("preferences database is down"));
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, "pw",
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
        _identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(_user, "pw", Request(), cancelled.Token));

        _smtpFactory.Verify(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_AnIdentityStoreTimingOutAsCancelled_StillSends()
    {
        var sender = CreateSender();
        // The caller never cancelled: a preferences-layer timeout is an outage, so the send degrades.
        _identities.Setup(i => i.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        var result = await sender.SendAsync(_user, "pw", Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Mick", ((MailboxAddress)sent!.From[0]).Name);
    }

    [Fact]
    public async Task SendAsync_NoFromAddress_KeepsThePrimaryBehaviour()
    {
        var sender = CreateSender();
        MimeMessage? sent = null;
        _smtp.Setup(s => s.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MimeMessage, CancellationToken>((m, _) => sent = m)
            .ReturnsAsync(Result.Success());

        Assert.True((await sender.SendAsync(_user, "pw", Request(), CancellationToken.None)).IsSuccess);
        Assert.Equal("mick@weesky.be", Assert.IsType<MailboxAddress>(sent!.From[0]).Address);
    }
}
