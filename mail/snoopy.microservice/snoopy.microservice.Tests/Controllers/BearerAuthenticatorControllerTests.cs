using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class BearerAuthenticatorControllerTests
    {
        private readonly Mock<IUserAuthenticator> _authenticator = new();

        private BearerAuthenticatorController CreateController()
        {
            var controller = new BearerAuthenticatorController(_authenticator.Object);
            controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");
            return controller;
        }

        [Fact]
        public void Authenticate_WithValidCredentials_Returns200WithToken()
        {
            var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
            _authenticator.Setup(a => a.Authenticate("user@domain.com", "pass"))
                .Returns(Result.Success(token));

            var result = CreateController().Authenticate(new Credentials { Email = "user@domain.com", Password = "pass" });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(token, ok.Value);
        }

        [Fact]
        public void Authenticate_WithInvalidCredentials_Returns401()
        {
            _authenticator.Setup(a => a.Authenticate(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Result.Failure<AuthToken>("Authentication failed"));

            var result = CreateController().Authenticate(new Credentials { Email = "user@domain.com", Password = "wrong" });

            var status = Assert.IsType<StatusCodeResult>(result.Result);
            Assert.Equal(401, status.StatusCode);
        }

        [Fact]
        public void Authenticate_PassesEmailAndPasswordToAuthenticator()
        {
            _authenticator.Setup(a => a.Authenticate(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Result.Failure<AuthToken>("fail"));

            CreateController().Authenticate(new Credentials { Email = "exact@email.com", Password = "exactpass" });

            _authenticator.Verify(a => a.Authenticate("exact@email.com", "exactpass"), Times.Once);
        }
    }
}
