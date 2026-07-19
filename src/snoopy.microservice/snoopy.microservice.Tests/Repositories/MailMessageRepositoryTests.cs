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
    public class MailMessageRepositoryTests
    {
        private static readonly User Alice = new("alice@weesky.be");

        private static (MailMessageRepository repo, Mock<IImapConnectionFactory> factory, Mock<IImapSession> session) CreateSut()
        {
            var session = new Mock<IImapSession>();
            session.SetupGet(s => s.DirectorySeparator).Returns('/');

            var factory = new Mock<IImapConnectionFactory>();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success<IImapSession>(session.Object));

            var repo = new MailMessageRepository(factory.Object, Mock.Of<ILogger<MailMessageRepository>>());
            return (repo, factory, session);
        }

        [Fact]
        public async Task ListAsync_DelegatesToTheSessionWithTheRequestedPage()
        {
            var (repo, _, session) = CreateSut();
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
            var (repo, factory, session) = CreateSut();
            session.Setup(s => s.ListMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(new MailFolderPage()));

            await repo.ListAsync(Alice, "hunter2", "INBOX", 0, 50, CancellationToken.None);

            factory.Verify(f => f.OpenAsync("alice@weesky.be", "hunter2", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ListAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Mail authentication failed"));

            var result = await repo.ListAsync(Alice, "wrong", "INBOX", 0, 50, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Mail authentication failed", result.Error);
        }

        [Fact]
        public async Task ListAsync_DisposesTheSession()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.ListMessagesAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(new MailFolderPage()));

            await repo.ListAsync(Alice, "hunter2", "INBOX", 0, 50, CancellationToken.None);

            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task ListAsync_ThrowsWhenUserIsNull()
        {
            var (repo, _, _) = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.ListAsync(null!, "hunter2", "INBOX", 0, 50, CancellationToken.None));
        }
    }
}
