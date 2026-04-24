using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class AccountControllerTests
    {
        private readonly Mock<IUsersRepository> _usersRepo = new();
        private readonly Mock<IDovecotQuotaClient> _dovecotClient = new();

        private AccountController CreateController()
        {
            var controller = new AccountController(_usersRepo.Object, _dovecotClient.Object);
            controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
            return controller;
        }

        [Fact]
        public void GetAccountInfo_WhenUserFound_Returns200WithAccountInfo()
        {
            var accountInfo = new AccountInfo { UserId = 1, UserName = "john" };
            _usersRepo.Setup(r => r.GetAccountInfo(It.IsAny<User>()))
                .Returns(Result.Success(accountInfo));

            var result = CreateController().GetAccountInfo();

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(accountInfo, ok.Value);
        }

        [Fact]
        public void GetAccountInfo_WhenUserNotFound_Returns404()
        {
            _usersRepo.Setup(r => r.GetAccountInfo(It.IsAny<User>()))
                .Returns(Result.Failure<AccountInfo>("Account not found"));

            var result = CreateController().GetAccountInfo();

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(404, status.StatusCode);
        }

        [Fact]
        public async Task GetQuota_WhenSuccess_Returns200WithQuota()
        {
            var quota = new Quota { StorageBytesUsed = 1024, MessageCount = 5 };
            _dovecotClient.Setup(c => c.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(quota));

            var result = await CreateController().GetQuota(CancellationToken.None);

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(quota, ok.Value);
        }

        [Fact]
        public async Task GetQuota_WhenFailed_Returns502()
        {
            _dovecotClient.Setup(c => c.GetQuotaAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<Quota>("Unreachable"));

            var result = await CreateController().GetQuota(CancellationToken.None);

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(502, status.StatusCode);
        }

        [Fact]
        public void ChangePassword_WhenSuccess_Returns204()
        {
            _usersRepo.Setup(r => r.ChangePassword(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Result.Success());

            var result = CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "OldPass" });

            var status = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(204, status.StatusCode);
        }

        [Fact]
        public void ChangePassword_WhenFailed_Returns400WithEnvelope()
        {
            _usersRepo.Setup(r => r.ChangePassword(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Result.Failure("Invalid password"));

            var result = CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" });

            var obj = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, obj.StatusCode);
        }

        [Fact]
        public void ChangePassword_WhenFailed_EnvelopeContainsErrorMessage()
        {
            _usersRepo.Setup(r => r.ChangePassword(It.IsAny<User>(), It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Result.Failure("Invalid password"));

            var result = CreateController().ChangePassword(new SecretChange { NewPassword = "NewPass123!", OldPassword = "Wrong" });

            var obj = Assert.IsType<ObjectResult>(result);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("Invalid password", envelope.Message);
            Assert.Equal(ResultState.Error, envelope.State);
        }
    }
}
