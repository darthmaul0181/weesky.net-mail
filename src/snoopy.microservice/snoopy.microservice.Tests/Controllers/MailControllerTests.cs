using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class MailControllerTests
    {
        private readonly Mock<IMailFolderRepository> _folders = new();
        private readonly Mock<IMailMessageRepository> _messages = new();
        private readonly Mock<IMailCredentialStore> _credentials = new();

        private MailController CreateController()
        {
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));

            return new MailController(_folders.Object, _messages.Object, _credentials.Object)
            {
                ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("alice", "weesky.be")
            };
        }

        private void SetupTree(params MailFolderNode[] nodes)
            => _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                       .ReturnsAsync(Result.Success<IReadOnlyList<MailFolderNode>>(nodes.ToList()));

        [Fact]
        public async Task GetFolders_ReturnsTheTree()
        {
            SetupTree(new MailFolderNode { Path = "INBOX", Name = "INBOX", SpecialUse = "inbox" });

            var result = await CreateController().GetFolders(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var tree = Assert.IsAssignableFrom<IReadOnlyList<MailFolderNode>>(ok.Value);
            Assert.Single(tree);
        }

        [Fact]
        public async Task GetFolders_PassesTheDecryptedPasswordToTheRepository()
        {
            SetupTree();

            await CreateController().GetFolders(CancellationToken.None);

            _folders.Verify(f => f.GetTreeAsync(It.IsAny<User>(), "hunter2", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetFolders_Returns401WhenCredentialsAreUnavailable()
        {
            var controller = CreateController();
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                        .Returns(Result.Failure<string>("credentials_unavailable"));

            var result = await controller.GetFolders(CancellationToken.None);

            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result.Result);
            var envelope = Assert.IsType<ResultEnveloppe>(unauthorized.Value);
            Assert.Equal("credentials_unavailable", envelope.Message);
        }

        [Fact]
        public async Task GetFolders_DoesNotReachTheRepositoryWithoutCredentials()
        {
            var controller = CreateController();
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                        .Returns(Result.Failure<string>("credentials_unavailable"));

            await controller.GetFolders(CancellationToken.None);

            _folders.Verify(
                f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task GetFolders_Returns502WhenImapFails()
        {
            _folders.Setup(f => f.GetTreeAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure<IReadOnlyList<MailFolderNode>>("Unable to connect to the mail service"));

            var result = await CreateController().GetFolders(CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        [Fact]
        public async Task GetFolders_NeverLeaksTheCredentialsIntoTheResponse()
        {
            SetupTree(new MailFolderNode { Path = "INBOX", Name = "INBOX" });

            var result = await CreateController().GetFolders(CancellationToken.None);

            var payload = System.Text.Json.JsonSerializer.Serialize(
                Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.DoesNotContain("hunter2", payload);
        }

        // ── Create ──────────────────────────────────────────────────────────

        [Fact]
        public async Task CreateFolder_ReturnsTheNewPath()
        {
            _folders.Setup(f => f.CreateFolderAsync(It.IsAny<User>(), "hunter2", "INBOX", "Projects", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success("INBOX/Projects"));

            var result = await CreateController().CreateFolder(
                new CreateFolderRequest { ParentPath = "INBOX", Name = "Projects" }, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal("INBOX/Projects", ok.Value);
        }

        [Fact]
        public async Task CreateFolder_Returns400ForABlankNameWithoutReachingTheRepository()
        {
            var result = await CreateController().CreateFolder(
                new CreateFolderRequest { ParentPath = "", Name = "   " }, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            _folders.Verify(f => f.CreateFolderAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task CreateFolder_Returns401WhenCredentialsAreUnavailable()
        {
            var controller = CreateController();
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                        .Returns(Result.Failure<string>("credentials_unavailable"));

            var result = await controller.CreateFolder(
                new CreateFolderRequest { Name = "Projects" }, CancellationToken.None);

            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }

        [Fact]
        public async Task CreateFolder_Returns502WhenImapFails()
        {
            _folders.Setup(f => f.CreateFolderAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure<string>("Unable to create the folder"));

            var result = await CreateController().CreateFolder(
                new CreateFolderRequest { Name = "Projects" }, CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        // ── Rename ──────────────────────────────────────────────────────────

        [Fact]
        public async Task RenameFolder_ReturnsThePath()
        {
            _folders.Setup(f => f.RenameFolderAsync(It.IsAny<User>(), "hunter2", "Old", "INBOX", "New", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success("INBOX/New"));

            var result = await CreateController().RenameFolder(
                new RenameFolderRequest { Path = "Old", NewParentPath = "INBOX", NewName = "New" }, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal("INBOX/New", ok.Value);
        }

        [Fact]
        public async Task RenameFolder_Returns400WhenPathOrNameIsMissing()
        {
            var controller = CreateController();

            Assert.IsType<BadRequestObjectResult>(
                (await controller.RenameFolder(new RenameFolderRequest { Path = "", NewName = "New" }, CancellationToken.None)).Result);
            Assert.IsType<BadRequestObjectResult>(
                (await controller.RenameFolder(new RenameFolderRequest { Path = "Old", NewName = " " }, CancellationToken.None)).Result);

            _folders.Verify(f => f.RenameFolderAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        // ── Delete ──────────────────────────────────────────────────────────

        [Fact]
        public async Task DeleteFolder_Returns204OnSuccess()
        {
            _folders.Setup(f => f.DeleteFolderAsync(It.IsAny<User>(), "hunter2", "Projects", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success());

            var result = await CreateController().DeleteFolder(
                new DeleteFolderRequest { Path = "Projects" }, CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
        }

        [Fact]
        public async Task DeleteFolder_Returns400ForABlankPathWithoutReachingTheRepository()
        {
            var result = await CreateController().DeleteFolder(
                new DeleteFolderRequest { Path = "" }, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
            _folders.Verify(f => f.DeleteFolderAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task DeleteFolder_Returns502WhenTheServerRefuses()
        {
            _folders.Setup(f => f.DeleteFolderAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Failure("The inbox cannot be deleted"));

            var result = await CreateController().DeleteFolder(
                new DeleteFolderRequest { Path = "INBOX" }, CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        // ── Subscription ────────────────────────────────────────────────────

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task SetFolderSubscription_Returns204AndPassesTheState(bool subscribed)
        {
            _folders.Setup(f => f.SetSubscriptionAsync(It.IsAny<User>(), "hunter2", "Projects", subscribed, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Result.Success());

            var result = await CreateController().SetFolderSubscription(
                new FolderSubscriptionRequest { Path = "Projects", Subscribed = subscribed }, CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status204NoContent, status.StatusCode);
            _folders.Verify(f => f.SetSubscriptionAsync(
                It.IsAny<User>(), "hunter2", "Projects", subscribed, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SetFolderSubscription_Returns401WhenCredentialsAreUnavailable()
        {
            var controller = CreateController();
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                        .Returns(Result.Failure<string>("credentials_unavailable"));

            var result = await controller.SetFolderSubscription(
                new FolderSubscriptionRequest { Path = "Projects", Subscribed = true }, CancellationToken.None);

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        // ── Messages ────────────────────────────────────────────────────────

        [Fact]
        public async Task GetMessages_ReturnsThePage()
        {
            _messages.Setup(m => m.ListAsync(It.IsAny<User>(), "hunter2", "INBOX", 0, 50, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Success(new MailFolderPage
                     {
                         FolderPath = "INBOX",
                         UidValidity = 42,
                         Total = 1,
                         Messages = { new MailMessageSummary { Uid = 7, Subject = "Hello" } }
                     }));

            var result = await CreateController().GetMessages("INBOX", 0, 50, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            var page = Assert.IsType<MailFolderPage>(ok.Value);
            Assert.Equal(42u, page.UidValidity);
            Assert.Single(page.Messages);
        }

        [Fact]
        public async Task GetMessages_Returns400ForABlankFolder()
        {
            var result = await CreateController().GetMessages("  ", 0, 50, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            VerifyMessagesNeverCalled();
        }

        [Fact]
        public async Task GetMessages_Returns400ForANegativePage()
        {
            var result = await CreateController().GetMessages("INBOX", -1, 50, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            VerifyMessagesNeverCalled();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(201)]
        public async Task GetMessages_Returns400ForAPageSizeOutOfRange(int pageSize)
        {
            var result = await CreateController().GetMessages("INBOX", 0, pageSize, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
            VerifyMessagesNeverCalled();
        }

        [Fact]
        public async Task GetMessages_Returns401WhenCredentialsAreUnavailable()
        {
            var controller = CreateController();
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                        .Returns(Result.Failure<string>("credentials_unavailable"));

            var result = await controller.GetMessages("INBOX", 0, 50, CancellationToken.None);

            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetMessages_Returns502WhenImapFails()
        {
            _messages.Setup(m => m.ListAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Failure<MailFolderPage>("Unable to read the messages"));

            var result = await CreateController().GetMessages("INBOX", 0, 50, CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        private void VerifyMessagesNeverCalled()
            => _messages.Verify(m => m.ListAsync(
                It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
                Times.Never);

        // ── Message detail ──────────────────────────────────────────────────

        [Fact]
        public async Task GetMessage_ReturnsTheDetail()
        {
            _messages.Setup(m => m.GetAsync(It.IsAny<User>(), "hunter2", "INBOX", 42u, It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Success(new MailMessageDetail { Uid = 42, Subject = "Re: facture" }));

            var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Equal("Re: facture", Assert.IsType<MailMessageDetail>(ok.Value).Subject);
        }

        [Fact]
        public async Task GetMessage_Returns404WhenTheUidDoesNotResolve()
        {
            _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Failure<MailMessageDetail>(ImapSession.MessageNotFound));

            var result = await CreateController().GetMessage("INBOX", 999, CancellationToken.None);

            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetMessage_Returns502ForAnyOtherFailure()
        {
            _messages.Setup(m => m.GetAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Failure<MailMessageDetail>("Unable to read the message"));

            var result = await CreateController().GetMessage("INBOX", 42, CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }

        [Fact]
        public async Task GetMessage_Returns400ForABlankFolder()
        {
            var result = await CreateController().GetMessage("", 42, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result.Result);
        }

        [Fact]
        public async Task GetMessage_Returns401WhenCredentialsAreUnavailable()
        {
            var controller = CreateController();
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                        .Returns(Result.Failure<string>("credentials_unavailable"));

            var result = await controller.GetMessage("INBOX", 42, CancellationToken.None);

            Assert.IsType<UnauthorizedObjectResult>(result.Result);
        }

        // ── Attachment ──────────────────────────────────────────────────────

        [Fact]
        public async Task GetAttachment_ReturnsTheFileWithAnAttachmentDisposition()
        {
            _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), "hunter2", "INBOX", 42u, "2", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Success(new MailAttachmentContent
                     {
                         Content = new byte[] { 1, 2, 3 },
                         FileName = "report.pdf",
                         ContentType = "application/pdf"
                     }));

            var result = await CreateController().GetAttachment("INBOX", 42, "2", CancellationToken.None);

            var file = Assert.IsType<FileContentResult>(result);
            Assert.Equal("application/pdf", file.ContentType);
            Assert.Equal("report.pdf", file.FileDownloadName);
            Assert.Equal(new byte[] { 1, 2, 3 }, file.FileContents);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task GetAttachment_Returns400ForABlankPart(string part)
        {
            var result = await CreateController().GetAttachment("INBOX", 42, part, CancellationToken.None);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task GetAttachment_Returns404WhenThePartDoesNotResolve()
        {
            _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Failure<MailAttachmentContent>(ImapSession.AttachmentNotFound));

            var result = await CreateController().GetAttachment("INBOX", 42, "99", CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status404NotFound, status.StatusCode);
        }

        [Fact]
        public async Task GetAttachment_Returns502ForAnyOtherFailure()
        {
            _messages.Setup(m => m.GetAttachmentAsync(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<uint>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(Result.Failure<MailAttachmentContent>("Unable to read the attachment"));

            var result = await CreateController().GetAttachment("INBOX", 42, "2", CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
        }
    }
}
