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

        [Fact]
        public async Task GetAsync_DelegatesToTheSessionAndDisposesIt()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.GetMessageAsync("INBOX", 42u, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Success(new MailMessageDetail { Uid = 42, Subject = "Hello" }));

            var result = await repo.GetAsync(Alice, "hunter2", "INBOX", 42, CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal("Hello", result.Value.Subject);
            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task GetAsync_PropagatesTheNotFoundSentinel()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.GetMessageAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound));

            var result = await repo.GetAsync(Alice, "hunter2", "INBOX", 999, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ImapSession.MessageNotFound, result.Error);
        }

        [Fact]
        public async Task GetAsync_PropagatesAConnectionFailure()
        {
            var (repo, factory, _) = CreateSut();
            factory.Setup(f => f.OpenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<IImapSession>("Unable to connect to the mail service"));

            var result = await repo.GetAsync(Alice, "hunter2", "INBOX", 42, CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Unable to connect to the mail service", result.Error);
        }

        [Fact]
        public async Task GetAttachmentAsync_DelegatesToTheSessionAndDisposesIt()
        {
            var (repo, _, session) = CreateSut();
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
            session.Verify(s => s.DisposeAsync(), Times.Once);
        }

        [Fact]
        public async Task GetAttachmentAsync_PropagatesTheAttachmentNotFoundSentinel()
        {
            var (repo, _, session) = CreateSut();
            session.Setup(s => s.GetAttachmentAsync(It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound));

            var result = await repo.GetAttachmentAsync(Alice, "hunter2", "INBOX", 42, "99", CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal(ImapSession.AttachmentNotFound, result.Error);
        }

        [Fact]
        public async Task MessageReads_ThrowWhenUserIsNull()
        {
            var (repo, _, _) = CreateSut();

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.GetAsync(null!, "p", "INBOX", 1, CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentNullException>(
                () => repo.GetAttachmentAsync(null!, "p", "INBOX", 1, "2", CancellationToken.None));
        }
    }
}
