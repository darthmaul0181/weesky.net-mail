using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers
{
    public class LoginControllerTests
    {
        private static readonly TokenConstants TestTokenConstants = new()
        {
            Issuer = "issuer",
            Audience = "audience",
            ExpiryInMinutes = 30,
            Key = "signing-key-long-enough-for-hmac256",
            AuthCookieName = "BearerAuth"
        };

        private readonly Mock<IUserAuthenticator> _authenticator = new();
        private readonly Mock<IMailCredentialStore> _credentialStore = new();

        private LoginController CreateController(DefaultHttpContext? httpContext = null)
        {
            var controller = new LoginController(_authenticator.Object, Options.Create(TestTokenConstants), _credentialStore.Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext ?? new DefaultHttpContext()
            };
            return controller;
        }

        [Fact]
        public async Task Login_WithValidCredentials_Returns200WithToken()
        {
            var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
            _authenticator.Setup(a => a.AuthenticateAsync("user@domain.com", "pass"))
                .ReturnsAsync(Result.Success(token));

            var result = await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "pass" });

            var ok = Assert.IsType<OkObjectResult>(result.Result);
            Assert.Same(token, ok.Value);
        }

        [Fact]
        public async Task Login_WithValidCredentials_SetsAuthCookie()
        {
            var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
            _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Success(token));
            var httpContext = new DefaultHttpContext();

            await CreateController(httpContext).Login(new Credentials { Email = "user@domain.com", Password = "pass" });

            Assert.True(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
        }

        [Fact]
        public async Task Login_WithInvalidCredentials_Returns401()
        {
            _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Failure<AuthToken>("Authentication failed"));

            var result = await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "wrong" });

            var obj = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(401, obj.StatusCode);
            var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
            Assert.Equal("Authentication failed", envelope.Message);
        }

        [Fact]
        public async Task Login_WithInvalidCredentials_DoesNotSetCookie()
        {
            _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Failure<AuthToken>("Authentication failed"));
            var httpContext = new DefaultHttpContext();

            await CreateController(httpContext).Login(new Credentials { Email = "user@domain.com", Password = "wrong" });

            Assert.False(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
        }

        [Fact]
        public void Logout_Returns204()
        {
            var controller = new LoginController(_authenticator.Object, Options.Create(TestTokenConstants), _credentialStore.Object);
            controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");

            var result = controller.Logout();

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task Login_OnSuccess_StoresTheCredentialsCookie()
        {
            _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Success(new AuthToken { ExpiresIn = 30, Token = "jwt.token" }));

            await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "hunter2" });

            _credentialStore.Verify(
                s => s.Store(It.IsAny<HttpResponse>(), "hunter2", TimeSpan.FromMinutes(30)),
                Times.Once);
        }

        [Fact]
        public async Task Login_OnFailure_DoesNotStoreTheCredentialsCookie()
        {
            _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(Result.Failure<AuthToken>("Invalid credentials"));

            await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "wrong" });

            _credentialStore.Verify(
                s => s.Store(It.IsAny<HttpResponse>(), It.IsAny<string>(), It.IsAny<TimeSpan>()),
                Times.Never);
        }

        [Fact]
        public void Logout_ClearsTheCredentialsCookie()
        {
            var controller = new LoginController(_authenticator.Object, Options.Create(TestTokenConstants), _credentialStore.Object);
            controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");

            controller.Logout();

            _credentialStore.Verify(s => s.Clear(It.IsAny<HttpResponse>()), Times.Once);
        }
    }
}
