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
    }
}
