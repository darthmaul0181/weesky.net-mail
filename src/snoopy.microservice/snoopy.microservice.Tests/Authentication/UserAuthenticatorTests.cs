using Microsoft.Extensions.Logging;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication
{
    public class UserAuthenticatorTests
    {
        private readonly Mock<IUsersRepository> _usersRepo = new();
        private readonly Mock<ITokenManager> _tokenManager = new();

        private UserAuthenticator CreateSut() =>
            new(_usersRepo.Object, _tokenManager.Object, Mock.Of<ILogger<UserAuthenticator>>());

        [Fact]
        public void Authenticate_WithUnknownUser_ReturnsFailure()
        {
            _usersRepo.Setup(r => r.FindByEmail(It.IsAny<string>())).Returns((User)null!);

            var result = CreateSut().Authenticate("unknown@example.com", "password");

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void Authenticate_WithBadPassword_ReturnsFailure()
        {
            var user = new User("john@example.com");
            _usersRepo.Setup(r => r.FindByEmail("john@example.com")).Returns(user);
            _usersRepo.Setup(r => r.IsValidPassword(user, "wrong")).Returns(false);

            var result = CreateSut().Authenticate("john@example.com", "wrong");

            Assert.True(result.IsFailure);
        }

        [Fact]
        public void Authenticate_WithValidCredentials_ReturnsSuccess()
        {
            var user = new User("john@example.com");
            var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
            _usersRepo.Setup(r => r.FindByEmail("john@example.com")).Returns(user);
            _usersRepo.Setup(r => r.IsValidPassword(user, "correct")).Returns(true);
            _tokenManager.Setup(t => t.Generate(user)).Returns(token);

            var result = CreateSut().Authenticate("john@example.com", "correct");

            Assert.True(result.IsSuccess);
        }

        [Fact]
        public void Authenticate_WithValidCredentials_ReturnsGeneratedToken()
        {
            var user = new User("john@example.com");
            var expected = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
            _usersRepo.Setup(r => r.FindByEmail("john@example.com")).Returns(user);
            _usersRepo.Setup(r => r.IsValidPassword(user, "correct")).Returns(true);
            _tokenManager.Setup(t => t.Generate(user)).Returns(expected);

            var result = CreateSut().Authenticate("john@example.com", "correct");

            Assert.Same(expected, result.Value);
        }

        [Fact]
        public void Authenticate_WithUnknownUser_NeverCallsTokenManager()
        {
            _usersRepo.Setup(r => r.FindByEmail(It.IsAny<string>())).Returns((User)null!);

            CreateSut().Authenticate("unknown@example.com", "password");

            _tokenManager.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public void Authenticate_WithBadPassword_NeverCallsTokenManager()
        {
            var user = new User("john@example.com");
            _usersRepo.Setup(r => r.FindByEmail(It.IsAny<string>())).Returns(user);
            _usersRepo.Setup(r => r.IsValidPassword(user, It.IsAny<string>())).Returns(false);

            CreateSut().Authenticate("john@example.com", "wrong");

            _tokenManager.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
        }
    }
}
