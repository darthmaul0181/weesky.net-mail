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
        private readonly Mock<IMailCredentialStore> _credentials = new();

        private MailController CreateController()
        {
            _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));

            return new MailController(_folders.Object, _credentials.Object)
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
    }
}
