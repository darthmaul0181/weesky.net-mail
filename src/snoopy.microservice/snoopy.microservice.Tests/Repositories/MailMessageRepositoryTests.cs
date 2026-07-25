using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using MimeKit;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class MailMessageRepositoryTests
{
    private static readonly User Alice = new("alice@weesky.be");

    private static (MailMessageRepository repo, Mock<IImapSessionProvider> sessions, Mock<IImapSession> session) CreateSut()
    {
        var session = new Mock<IImapSession>();
        session.SetupGet(s => s.DirectorySeparator).Returns('/');

        var sessions = new Mock<IImapSessionProvider>();
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success<IImapSession>(session.Object));

        var repo = new MailMessageRepository(sessions.Object);
        return (repo, sessions, session);
    }

    [Fact]
    public async Task ListAsync_DelegatesToTheSessionWithTheRequestedPage()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.ListMessagesAsync("INBOX", 2, 25, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailFolderPage { FolderPath = "INBOX", Page = 2, PageSize = 25 }));

        var result = await repo.ListAsync(Alice, "hunter2", "INBOX", 2, 25, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Page);
        session.Verify(s => s.ListMessagesAsync("INBOX", 2, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_OpensTheSessionForTheAuthenticatedUser()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.ListMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailFolderPage()));

        await repo.ListAsync(Alice, "hunter2", "INBOX", 0, 50, CancellationToken.None);

        sessions.Verify(f => f.GetAsync("alice@weesky.be", "hunter2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));

        var result = await repo.ListAsync(Alice, "wrong", "INBOX", 0, 50, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Mail authentication failed", result.Error);
    }

    [Fact]
    public async Task ListAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.ListMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailFolderPage()));

        await repo.ListAsync(Alice, "hunter2", "INBOX", 0, 50, CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.ListAsync(null!, "hunter2", "INBOX", 0, 50, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_DelegatesToTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.GetMessageAsync("INBOX", 42u, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailMessageDetail { Uid = 42, Subject = "Hello" }));

        var result = await repo.GetAsync(Alice, "hunter2", "INBOX", 42, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Hello", result.Value.Subject);
        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_PropagatesTheNotFoundSentinel()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.GetMessageAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound));

        var result = await repo.GetAsync(Alice, "hunter2", "INBOX", 999, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ImapSession.MessageNotFound, result.Error);
    }

    [Fact]
    public async Task GetAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

        var result = await repo.GetAsync(Alice, "hunter2", "INBOX", 42, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Unable to connect to the mail service", result.Error);
    }

    [Fact]
    public async Task GetAttachmentAsync_DelegatesToTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.GetAttachmentAsync("INBOX", 42u, "2", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailAttachmentContent
               {
                   Content = new byte[] { 1, 2, 3 },
                   FileName = "report.pdf",
                   ContentType = "application/pdf"
               }));

        var result = await repo.GetAttachmentAsync(Alice, "hunter2", "INBOX", 42, "2", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("report.pdf", result.Value.FileName);
        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAttachmentAsync_PropagatesTheAttachmentNotFoundSentinel()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.GetAttachmentAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound));

        var result = await repo.GetAttachmentAsync(Alice, "hunter2", "INBOX", 42, "99", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ImapSession.AttachmentNotFound, result.Error);
    }

    [Fact]
    public async Task MessageReads_ThrowWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.GetAsync(null!, "p", "INBOX", 1, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.GetAttachmentAsync(null!, "p", "INBOX", 1, "2", CancellationToken.None));
    }

    [Fact]
    public async Task SetFlagsAsync_DelegatesToTheSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.SetFlagsAsync("INBOX", It.IsAny<IReadOnlyList<uint>>(), MailFlag.Seen, true, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.SetFlagsAsync(Alice, "pw", "INBOX", [1u, 2u], MailFlag.Seen, true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        session.Verify(s => s.SetFlagsAsync("INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 1, 2 })),
            MailFlag.Seen, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFlagsAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("down"));

        var result = await repo.SetFlagsAsync(Alice, "pw", "INBOX", [1u], MailFlag.Flagged, false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("down", result.Error);
    }

    [Fact]
    public async Task SetFlagsAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.SetFlagsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<uint>>(), It.IsAny<MailFlag>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        await repo.SetFlagsAsync(Alice, "pw", "INBOX", [1u], MailFlag.Seen, true, CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetFlagsAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.SetFlagsAsync(null!, "pw", "INBOX", [1u], MailFlag.Seen, true, CancellationToken.None));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MoveOrCopyAsync_DelegatesToTheSession(bool copy)
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.MoveOrCopyAsync("INBOX", It.IsAny<IReadOnlyList<uint>>(), "Archive", copy, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.MoveOrCopyAsync(Alice, "pw", "INBOX", [1u, 2u], "Archive", copy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        session.Verify(s => s.MoveOrCopyAsync("INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 1, 2 })),
            "Archive", copy, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoveOrCopyAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("down"));

        var result = await repo.MoveOrCopyAsync(Alice, "pw", "INBOX", [1u], "Archive", false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("down", result.Error);
    }

    [Fact]
    public async Task MoveOrCopyAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.MoveOrCopyAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<uint>>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        await repo.MoveOrCopyAsync(Alice, "pw", "INBOX", [1u], "Archive", false, CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MoveOrCopyAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.MoveOrCopyAsync(null!, "pw", "INBOX", [1u], "Archive", false, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToTheSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.DeleteAsync("INBOX", It.IsAny<IReadOnlyList<uint>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.DeleteAsync(Alice, "pw", "INBOX", [1u, 2u], CancellationToken.None);

        Assert.True(result.IsSuccess);
        session.Verify(s => s.DeleteAsync("INBOX",
            It.Is<IReadOnlyList<uint>>(u => u.SequenceEqual(new uint[] { 1, 2 })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("down"));

        var result = await repo.DeleteAsync(Alice, "pw", "INBOX", [1u], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("down", result.Error);
    }

    [Fact]
    public async Task DeleteAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<uint>>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        await repo.DeleteAsync(Alice, "pw", "INBOX", [1u], CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.DeleteAsync(null!, "pw", "INBOX", [1u], CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Trash")]
    public async Task EmptyAsync_DelegatesToTheSession(string? target)
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.EmptyAsync("INBOX", target, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.EmptyAsync(Alice, "pw", "INBOX", target, CancellationToken.None);

        Assert.True(result.IsSuccess);
        session.Verify(s => s.EmptyAsync("INBOX", target, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("down"));

        var result = await repo.EmptyAsync(Alice, "pw", "INBOX", null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("down", result.Error);
    }

    [Fact]
    public async Task EmptyAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.EmptyAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        await repo.EmptyAsync(Alice, "pw", "INBOX", "Trash", CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.EmptyAsync(null!, "pw", "INBOX", null, CancellationToken.None));
    }

    [Fact]
    public async Task SearchAsync_DelegatesToTheSession()
    {
        var (repo, sessions, session) = CreateSut();
        var criteria = new MailSearchCriteria("hello", null, null, null, null, null, false, false, false);
        var page = new MailSearchPage { Total = 1 };
        session.Setup(s => s.SearchAsync("INBOX", false, criteria, 0, 50, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(page));

        var result = await repo.SearchAsync(Alice, "pw", "INBOX", false, criteria, 0, 50, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(page, result.Value);
        session.Verify(s => s.SearchAsync("INBOX", false, criteria, 0, 50, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _) = CreateSut();
        var criteria = new MailSearchCriteria("hello", null, null, null, null, null, false, false, false);
        sessions.Setup(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("nope"));

        var result = await repo.SearchAsync(Alice, "pw", "INBOX", false, criteria, 0, 50, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("nope", result.Error);
    }

    [Fact]
    public async Task SearchAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        var criteria = new MailSearchCriteria("hello", null, null, null, null, null, false, false, false);
        session.Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<MailSearchCriteria>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailSearchPage()));

        await repo.SearchAsync(Alice, "pw", "INBOX", false, criteria, 0, 50, CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();
        var criteria = new MailSearchCriteria("hello", null, null, null, null, null, false, false, false);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.SearchAsync(null!, "pw", "INBOX", false, criteria, 0, 50, CancellationToken.None));
    }

    [Fact]
    public async Task SaveDraftAsync_DelegatesToTheSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.SaveDraftAsync("Drafts", It.IsAny<MimeMessage>(), 41u, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(42u));

        var result = await repo.SaveDraftAsync(Alice, "pw", "Drafts", new MimeMessage(), 41u, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(42u, result.Value);
    }

    [Fact]
    public async Task SaveDraftAsync_FailsWhenTheSessionCannotOpen()
    {
        var (repo, sessions, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(Alice.Email, "pw", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("boom"));

        var result = await repo.SaveDraftAsync(Alice, "pw", "Drafts", new MimeMessage(), null, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SaveDraftAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session) = CreateSut();
        session.Setup(s => s.SaveDraftAsync(It.IsAny<string>(), It.IsAny<MimeMessage>(), It.IsAny<uint?>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(1u));

        await repo.SaveDraftAsync(Alice, "pw", "Drafts", new MimeMessage(), null, CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDraftAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.SaveDraftAsync(null!, "pw", "Drafts", new MimeMessage(), null, CancellationToken.None));
    }
}
