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

public sealed class DraftSaverTests
{
    private static readonly MailAccountConnection Conn = TestConnections.Primary("mick@weesky.be", "pw");

    private readonly Mock<IUsersRepository> _users = new();
    private readonly Mock<IAliasesRepository> _aliases = new();
    private readonly Mock<ISendingIdentityStore> _identities = new();
    private readonly Mock<IOutgoingMailSanitizer> _sanitizer = new();
    private readonly Mock<IStagedAttachmentStore> _staged = new();
    private readonly Mock<IMailFolderRepository> _folders = new();
    private readonly Mock<IFolderRoleStore> _roles = new();
    private readonly Mock<IMailMessageRepository> _messages = new();
    private static readonly Guid WebmailUid = Guid.NewGuid();
    private readonly User _user = new("mick@weesky.be") { WebmailUid = WebmailUid };

    private DraftSaver CreateSaver()
    {
        _users.Setup(u => u.FindByEmailAsync("mick@weesky.be"))
            .ReturnsAsync(new User("mick@weesky.be") { FullName = "Mick" });
        _sanitizer.Setup(s => s.Prepare(It.IsAny<string>()))
            .Returns(new OutgoingBody("<div>hi</div>", "hi"));

        // A tree whose "Drafts" folder carries the server flag: the resolver finds the role.
        var drafts = new MailFolderNode { Name = "Drafts", Path = "Drafts", AttributeRole = "drafts", Selectable = true };
        _folders.Setup(f => f.GetTreeAsync(_user, Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([drafts]));
        _roles.Setup(r => r.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FolderRoleOverride>());
        _messages.Setup(m => m.SaveDraftAsync(_user, Conn, "Drafts", It.IsAny<MimeMessage>(), It.IsAny<uint?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(7u));

        _aliases.Setup(a => a.GetAliasesAsync(It.IsAny<User>()))
            .ReturnsAsync([new Alias { Name = "michel", Domain = "weesky.be" }]);
        _identities.Setup(i => i.GetAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // The real factory, not a mock: several tests assert on how the built message behaves.
        var factory = new OutgoingMessageFactory(_users.Object, _aliases.Object, _identities.Object,
            _sanitizer.Object, _staged.Object, NullLogger<OutgoingMessageFactory>.Instance);
        return new DraftSaver(factory, _folders.Object, _roles.Object, _messages.Object, NullLogger<DraftSaver>.Instance);
    }

    private static SaveDraftRequest Request() => new()
    {
        To = ["alice@example.com"], Subject = "Hi", HtmlBody = "<div>hi</div>"
    };

    [Fact]
    public async Task SaveDraft_AppendsToTheDraftsRoleFolder()
    {
        var saver = CreateSaver();

        var result = await saver.SaveAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7u, result.Value.Uid);
        Assert.Equal("Drafts", result.Value.FolderPath);
        _messages.Verify(m => m.SaveDraftAsync(_user, Conn, "Drafts", It.IsAny<MimeMessage>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDraft_PassesTheReplaceUid()
    {
        var saver = CreateSaver();

        await saver.SaveAsync(_user, Conn, Request() with { ReplaceUid = 41u }, CancellationToken.None);

        _messages.Verify(m => m.SaveDraftAsync(_user, Conn, "Drafts", It.IsAny<MimeMessage>(), 41u, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDraft_FailsWithoutADraftsFolder()
    {
        var saver = CreateSaver();
        _folders.Setup(f => f.GetTreeAsync(_user, Conn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(
                [new MailFolderNode { Name = "Stuff", Path = "Stuff", Selectable = true }]));

        var result = await saver.SaveAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IDraftSaver.NoDraftsFolder, result.Error);
        _messages.Verify(m => m.SaveDraftAsync(It.IsAny<User>(), It.IsAny<MailAccountConnection>(), It.IsAny<string>(),
            It.IsAny<MimeMessage>(), It.IsAny<uint?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveDraft_AcceptsAnEmptyDraft()
    {
        var saver = CreateSaver();
        var request = new SaveDraftRequest { To = [], Subject = string.Empty, HtmlBody = string.Empty };

        var result = await saver.SaveAsync(_user, Conn, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SaveDraft_RefusesAForeignFrom()
    {
        var saver = CreateSaver();

        var result = await saver.SaveAsync(_user, Conn,
            Request() with { FromAddress = "intruder@evil.com" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IOutgoingMessageFactory.ForbiddenFrom, result.Error);
    }

    [Fact]
    public async Task SaveDraft_KeepsStagedFilesAfterTheSave()
    {
        var id = Guid.NewGuid();
        var path = Path.GetTempFileName();
        try
        {
            _staged.Setup(s => s.Open(It.IsAny<string>(), id)).Returns(Result.Success(
                new StagedAttachment(new StagedAttachmentInfo(id, "a.txt", 4, "text/plain"), path)));
            var saver = CreateSaver();

            var result = await saver.SaveAsync(
                _user, Conn, Request() with { AttachmentIds = [id] }, CancellationToken.None);

            Assert.True(result.IsSuccess);
            _staged.Verify(s => s.Delete(It.IsAny<string>(), It.IsAny<Guid>()), Times.Never);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SaveDraft_DegradesRoleOverridesToServerFlags()
    {
        var saver = CreateSaver();
        _roles.Setup(r => r.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("preferences database is down"));

        var result = await saver.SaveAsync(_user, Conn, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Drafts", result.Value.FolderPath);
    }

    // The drafts role is per account: resolving a connected mailbox against the primary's
    // overrides files the draft into a folder the other server may not even have.
    [Fact]
    public async Task SaveDraft_ScopesTheRoleOverridesToTheConnectedAccount()
    {
        var accountId = Guid.NewGuid().ToString();
        var connected = TestConnections.Connected(accountId, "mick@external.test", "pw2");
        var saver = CreateSaver();
        var drafts = new MailFolderNode { Name = "Drafts", Path = "Drafts", AttributeRole = "drafts", Selectable = true };
        _folders.Setup(f => f.GetTreeAsync(_user, connected, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>([drafts]));
        _messages.Setup(m => m.SaveDraftAsync(_user, connected, "Drafts", It.IsAny<MimeMessage>(), It.IsAny<uint?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(9u));

        var result = await saver.SaveAsync(_user, connected, Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _roles.Verify(r => r.GetAsync(WebmailUid, accountId, It.IsAny<CancellationToken>()), Times.Once);
        _roles.Verify(r => r.GetAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<CancellationToken>()), Times.Never);
    }
}
