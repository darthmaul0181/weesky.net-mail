using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Data.Preferences;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories;

public sealed class MailFolderRepositoryTests
{
    private static readonly Guid AliceUid = Guid.NewGuid();
    private static readonly User Alice = new("alice@weesky.be") { WebmailUid = AliceUid };
    private static readonly MailAccountConnection Conn = TestConnections.Primary("alice@weesky.be", "hunter2");

    private static (MailFolderRepository repo, Mock<IImapSessionProvider> sessions,
                    Mock<IImapSession> session, Mock<IFolderRoleStore> store) CreateSut()
    {
        var session = new Mock<IImapSession>();
        session.SetupGet(s => s.DirectorySeparator).Returns('/');

        var sessions = new Mock<IImapSessionProvider>();
        sessions.Setup(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success<IImapSession>(session.Object));

        var store = new Mock<IFolderRoleStore>();

        var repo = new MailFolderRepository(sessions.Object, store.Object, Mock.Of<ILogger<MailFolderRepository>>());
        return (repo, sessions, session, store);
    }

    private static void SetupRename(Mock<IImapSession> session, string newPath, uint uidValidity = 42, string? mailboxId = null)
    {
        session.Setup(s => s.RenameFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(newPath));
        session.Setup(s => s.GetFolderStatusAsync(newPath, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailFolderStatus
               { Path = newPath, UidValidity = uidValidity, MailboxId = mailboxId, Selectable = true }));
    }

    private static void SetupTree(Mock<IImapSession> session, params MailFolderNode[] nodes)
        => session.Setup(s => s.ListFoldersAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(nodes.ToList()));

    [Fact]
    public async Task GetTreeAsync_ReturnsTheSessionTree()
    {
        var (repo, sessions, session, _) = CreateSut();
        SetupTree(session, new MailFolderNode { Path = "INBOX", Name = "INBOX", SpecialUse = "inbox", Unread = 4 });

        var result = await repo.GetTreeAsync(Alice, Conn, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("inbox", result.Value[0].SpecialUse);
    }

    [Fact]
    public async Task GetTreeAsync_OpensTheSessionForTheAuthenticatedUser()
    {
        var (repo, sessions, session, _) = CreateSut();
        SetupTree(session);

        await repo.GetTreeAsync(Alice, Conn, CancellationToken.None);

        sessions.Verify(f => f.GetAsync(Conn, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTreeAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));

        var result = await repo.GetTreeAsync(Alice, Conn, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Mail authentication failed", result.Error);
    }

    [Fact]
    public async Task GetTreeAsync_UsesTheRequestSession()
    {
        var (repo, sessions, session, _) = CreateSut();
        SetupTree(session);

        await repo.GetTreeAsync(Alice, Conn, CancellationToken.None);

        sessions.Verify(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTreeAsync_ThrowsWhenUserIsNull()
    {
        var (repo, sessions, _, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => repo.GetTreeAsync(null!, Conn, CancellationToken.None));
    }

    [Fact]
    public async Task CreateFolderAsync_DelegatesToTheRequestSession()
    {
        var (repo, sessions, session, _) = CreateSut();
        session.Setup(s => s.CreateFolderAsync("INBOX", "Projects", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success("INBOX/Projects"));

        var result = await repo.CreateFolderAsync(Alice, Conn, "INBOX", "Projects", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("INBOX/Projects", result.Value);
        sessions.Verify(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateFolderAsync_PropagatesTheSessionValidationFailure()
    {
        var (repo, sessions, session, _) = CreateSut();
        session.Setup(s => s.CreateFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<string>(ImapSession.InvalidFolderName));

        var result = await repo.CreateFolderAsync(Alice, Conn, "", "Pro/jects", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ImapSession.InvalidFolderName, result.Error);
    }

    [Fact]
    public async Task CreateFolderAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

        var result = await repo.CreateFolderAsync(Alice, Conn, "", "Projects", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Unable to connect to the mail service", result.Error);
    }

    [Fact]
    public async Task RenameFolderAsync_DelegatesToTheRequestSession()
    {
        var (repo, sessions, session, _) = CreateSut();
        session.Setup(s => s.RenameFolderAsync("Old", "INBOX", "New", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success("INBOX/New"));

        var result = await repo.RenameFolderAsync(Alice, Conn, "Old", "INBOX", "New", CancellationToken.None);

        Assert.True(result.IsSuccess);
        sessions.Verify(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameFolderAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));

        var result = await repo.RenameFolderAsync(Alice, Conn, "Old", "", "New", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // The separator handed to the store is the session's — '.' here, on purpose, because a
    // constant '/' would pass every test written against '/' and break on the home server.
    [Fact]
    public async Task Rename_UpdatesOverridesWithTheSessionSeparatorAndFreshIdentity()
    {
        var (repo, sessions, session, store) = CreateSut();
        session.SetupGet(s => s.DirectorySeparator).Returns('.');
        SetupRename(session, "Work", uidValidity: 42, mailboxId: "M-new");

        var result = await repo.RenameFolderAsync(Alice, Conn, "Projects", "", "Work", CancellationToken.None);

        Assert.True(result.IsSuccess);
        store.Verify(s => s.ApplyRenameAsync(AliceUid, AccountScope.Primary, "Projects", "Work", '.', 42UL, "M-new",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // The overrides are per account: renaming on a connected mailbox must never rewrite the
    // primary's rows, which name folders that do not exist on the other server.
    [Fact]
    public async Task Rename_MovesTheOverridesOfTheConnectedAccountOnly()
    {
        var accountId = Guid.NewGuid().ToString();
        var connected = TestConnections.Connected(accountId, "alice@external.test", "pw");
        var (repo, _, session, store) = CreateSut();
        SetupRename(session, "Work", uidValidity: 7, mailboxId: "M-new");

        var result = await repo.RenameFolderAsync(Alice, connected, "Projects", "", "Work", CancellationToken.None);

        Assert.True(result.IsSuccess);
        store.Verify(s => s.ApplyRenameAsync(AliceUid, accountId, "Projects", "Work", It.IsAny<char>(),
            7UL, "M-new", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.ApplyRenameAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<char>(), It.IsAny<ulong>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // IMAP is the source of truth: a failed bookkeeping write degrades to discovery via
    // the staleness guard instead of failing the operation the user asked for.
    [Fact]
    public async Task Rename_StillSucceedsWhenTheStoreWriteFails()
    {
        var (repo, sessions, session, store) = CreateSut();
        SetupRename(session, "Work");
        store.Setup(s => s.ApplyRenameAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<char>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await repo.RenameFolderAsync(Alice, Conn, "Projects", "", "Work", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Rename_SkipsTheStoreWhenTheStatusReReadFails()
    {
        var (repo, sessions, session, store) = CreateSut();
        session.Setup(s => s.RenameFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success("Work"));
        session.Setup(s => s.GetFolderStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<MailFolderStatus>("Unable to read the folder"));

        var result = await repo.RenameFolderAsync(Alice, Conn, "Projects", "", "Work", CancellationToken.None);

        Assert.True(result.IsSuccess);
        store.Verify(s => s.ApplyRenameAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<char>(), It.IsAny<ulong>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rename_TouchesNothingWhenImapRefuses()
    {
        var (repo, sessions, session, store) = CreateSut();
        session.Setup(s => s.RenameFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<string>("refused"));

        var result = await repo.RenameFolderAsync(Alice, Conn, "Projects", "", "Work", CancellationToken.None);

        Assert.True(result.IsFailure);
        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteFolderAsync_DelegatesToTheRequestSession()
    {
        var (repo, sessions, session, _) = CreateSut();
        session.Setup(s => s.DeleteFolderAsync("Projects", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.DeleteFolderAsync(Alice, Conn, "Projects", CancellationToken.None);

        Assert.True(result.IsSuccess);
        sessions.Verify(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteFolderAsync_PropagatesTheInboxRefusal()
    {
        var (repo, sessions, session, _) = CreateSut();
        session.Setup(s => s.DeleteFolderAsync("INBOX", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure("The inbox cannot be deleted"));

        var result = await repo.DeleteFolderAsync(Alice, Conn, "INBOX", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("The inbox cannot be deleted", result.Error);
    }

    [Fact]
    public async Task DeleteFolderAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

        var result = await repo.DeleteFolderAsync(Alice, Conn, "Projects", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Unable to connect to the mail service", result.Error);
    }

    [Theory]
    [InlineData('/')]
    [InlineData('.')]
    public async Task Delete_PurgesTheSubtreeOverrides(char separator)
    {
        var (repo, sessions, session, store) = CreateSut();
        session.SetupGet(s => s.DirectorySeparator).Returns(separator);
        session.Setup(s => s.DeleteFolderAsync("Projects", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.DeleteFolderAsync(Alice, Conn, "Projects", CancellationToken.None);

        Assert.True(result.IsSuccess);
        store.Verify(s => s.RemoveSubtreeAsync(AliceUid, AccountScope.Primary, "Projects", separator,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Same rule as the rename above: purging a subtree on a connected mailbox must not take the
    // primary's rows with it — they name folders that do not exist on the other server.
    [Fact]
    public async Task Delete_PurgesTheOverridesOfTheConnectedAccountOnly()
    {
        var accountId = Guid.NewGuid().ToString();
        var connected = TestConnections.Connected(accountId, "alice@external.test", "pw");
        var (repo, _, session, store) = CreateSut();
        session.Setup(s => s.DeleteFolderAsync("Projects", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.DeleteFolderAsync(Alice, connected, "Projects", CancellationToken.None);

        Assert.True(result.IsSuccess);
        store.Verify(s => s.RemoveSubtreeAsync(AliceUid, accountId, "Projects", It.IsAny<char>(),
            It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.RemoveSubtreeAsync(It.IsAny<Guid>(), AccountScope.Primary, It.IsAny<string>(),
            It.IsAny<char>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_TouchesNothingWhenImapRefuses()
    {
        var (repo, sessions, session, store) = CreateSut();
        session.Setup(s => s.DeleteFolderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure("The inbox cannot be deleted"));

        await repo.DeleteFolderAsync(Alice, Conn, "INBOX", CancellationToken.None);

        store.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetFolderStatus_PassesThroughTheSession()
    {
        var (repo, sessions, session, _) = CreateSut();
        session.Setup(s => s.GetFolderStatusAsync("Archive", It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success(new MailFolderStatus { Path = "Archive", UidValidity = 7 }));

        var result = await repo.GetFolderStatusAsync(Alice, Conn, "Archive", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7u, result.Value.UidValidity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetSubscriptionAsync_PassesTheDesiredState(bool subscribed)
    {
        var (repo, sessions, session, _) = CreateSut();
        session.Setup(s => s.SetSubscriptionAsync("Projects", subscribed, It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Success());

        var result = await repo.SetSubscriptionAsync(Alice, Conn, "Projects", subscribed, CancellationToken.None);

        Assert.True(result.IsSuccess);
        session.Verify(s => s.SetSubscriptionAsync("Projects", subscribed, It.IsAny<CancellationToken>()), Times.Once);
        sessions.Verify(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SetSubscriptionAsync_PropagatesAConnectionFailure()
    {
        var (repo, sessions, _, _) = CreateSut();
        sessions.Setup(f => f.GetAsync(It.IsAny<MailAccountConnection>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

        var result = await repo.SetSubscriptionAsync(Alice, Conn, "Projects", true, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task FolderMutations_ThrowWhenUserIsNull()
    {
        var (repo, sessions, _, _) = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.CreateFolderAsync(null!, Conn, "", "n", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.RenameFolderAsync(null!, Conn, "a", "", "b", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.DeleteFolderAsync(null!, Conn, "a", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SetSubscriptionAsync(null!, Conn, "a", true, CancellationToken.None));
    }
}
