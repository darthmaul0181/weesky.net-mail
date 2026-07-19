using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Repositories
{
    public class MailFolderRepositoryTests
    {
        private static readonly User Alice = new("alice@weesky.be");

        private static (MailFolderRepository repo, Mock<IImapConnectionFactory> factory, Mock<IImapSession> session) CreateSut()
        {
            var session = new Mock<IImapSession>();
            session.SetupGet(s => s.DirectorySeparator).Returns('/');

            var factory = new Mock<IImapConnectionFactory>();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IImapSession>(session.Object));

            var repo = new MailFolderRepository(factory.Object, Mock.Of<ILogger<MailFolderRepository>>());
            return (repo, factory, session);
        }

        private static void SetupTree(Mock<IImapSession> session, params MailFolderNode[] nodes)
            => session.Setup(s => s.ListFoldersAsync(It.IsAny<CancellationToken>()))
                      .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(nodes.ToList()));

        [Fact]
        public async Task GetTreeAsync_ReturnsTheSessionTree()
        {
            var (repo, _, session) = CreateSut();
            SetupTree(session, new MailFolderNode { Path = "INBOX", Name = "INBOX", SpecialUse = "inbox", Unread = 4 });

            var result = await repo.GetTreeAsync(Alice, "hunter2", CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Single(result.Value);
            Assert.Equal("inbox", result.Value[0].SpecialUse);
        }

        [Fact]
        public async Task GetTreeAsync_OpensTheSessionForTheAuthenticatedUser()
        {
            var (repo, factory, session) = CreateSut();
            SetupTree(session);

            await repo.GetTreeAsync(Alice, "hunter2", CancellationToken.None);

            factory.Verify(f => f.OpenAsync("alice@weesky.be", "hunter2", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetTreeAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));

            var result = await repo.GetTreeAsync(Alice, "wrong", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Mail authentication failed", result.Error);
        }

        [Fact]
        public async Task GetTreeAsync_DisposesTheSession()
        {
            var (repo, _, session) = CreateSut();
            SetupTree(session);

            await repo.GetTreeAsync(Alice, "hunter2", CancellationToken.None);

            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task GetTreeAsync_ThrowsWhenUserIsNull()
        {
            var (repo, _, _) = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.GetTreeAsync(null!, "hunter2", CancellationToken.None));
        }

        [Fact]
        public async Task CreateFolderAsync_DelegatesToTheSessionAndDisposesIt()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.CreateFolderAsync("INBOX", "Projects", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success("INBOX/Projects"));

            var result = await repo.CreateFolderAsync(Alice, "hunter2", "INBOX", "Projects", CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("INBOX/Projects", result.Value);
            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task CreateFolderAsync_PropagatesTheSessionValidationFailure()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.CreateFolderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<string>("A folder name cannot be empty or contain '/'"));

            var result = await repo.CreateFolderAsync(Alice, "hunter2", "", "Pro/jects", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Contains("cannot be empty or contain", result.Error);
        }

        [Fact]
        public async Task CreateFolderAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

            var result = await repo.CreateFolderAsync(Alice, "hunter2", "", "Projects", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Unable to connect to the mail service", result.Error);
        }

        [Fact]
        public async Task RenameFolderAsync_DelegatesToTheSessionAndDisposesIt()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.RenameFolderAsync("Old", "INBOX", "New", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success("INBOX/New"));

            var result = await repo.RenameFolderAsync(Alice, "hunter2", "Old", "INBOX", "New", CancellationToken.None);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task RenameFolderAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));

            var result = await repo.RenameFolderAsync(Alice, "hunter2", "Old", "", "New", CancellationToken.None);

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task DeleteFolderAsync_DelegatesToTheSessionAndDisposesIt()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.DeleteFolderAsync("Projects", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.DeleteFolderAsync(Alice, "hunter2", "Projects", CancellationToken.None);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task DeleteFolderAsync_PropagatesTheInboxRefusal()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.DeleteFolderAsync("INBOX", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure("The inbox cannot be deleted"));

            var result = await repo.DeleteFolderAsync(Alice, "hunter2", "INBOX", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("The inbox cannot be deleted", result.Error);
        }

        [Fact]
        public async Task DeleteFolderAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

            var result = await repo.DeleteFolderAsync(Alice, "hunter2", "Projects", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Unable to connect to the mail service", result.Error);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SetSubscriptionAsync_PassesTheDesiredState(bool subscribed)
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.SetSubscriptionAsync("Projects", subscribed, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success());

            var result = await repo.SetSubscriptionAsync(Alice, "hunter2", "Projects", subscribed, CancellationToken.None);

            Assert.True(result.IsSuccess);
            session.Verify(s => s.SetSubscriptionAsync("Projects", subscribed, It.IsAny<CancellationToken>()), Times.Once);
            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task SetSubscriptionAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

            var result = await repo.SetSubscriptionAsync(Alice, "hunter2", "Projects", true, CancellationToken.None);

            Assert.True(result.IsFailure);
        }

        [Fact]
        public async Task FolderMutations_ThrowWhenUserIsNull()
        {
            var (repo, _, _) = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.CreateFolderAsync(null!, "p", "", "n", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.RenameFolderAsync(null!, "p", "a", "", "b", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.DeleteFolderAsync(null!, "p", "a", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(() => repo.SetSubscriptionAsync(null!, "p", "a", true, CancellationToken.None));
        }
    }
}
