using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Repositories;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class UserAuthenticatorTests
{
    private readonly Mock<IUsersRepository> _usersRepo = new();
    private readonly Mock<ITokenManager> _tokenManager = new();
    private readonly Mock<IWebmailUserStore> _webmailUsers = new();

    private UserAuthenticator CreateSut() =>
        new(_usersRepo.Object, _tokenManager.Object, _webmailUsers.Object, Mock.Of<ILogger<UserAuthenticator>>());

    private static TokenManager RealTokenManager() => new(Options.Create(new TokenConstants
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpiryInMinutes = 30,
        Key = "test-signing-key-long-enough-for-hmac256",
        AuthCookieName = "BearerAuth"
    }));

    [Fact]
    public async Task Authenticate_WithUnknownUser_ReturnsFailure()
    {
        _usersRepo.Setup(r => r.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User)null!);

        var result = await CreateSut().AuthenticateAsync("unknown@example.com", "password");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Authenticate_WithBadPassword_ReturnsFailure()
    {
        var user = new User("john@example.com");
        _usersRepo.Setup(r => r.FindByEmailAsync("john@example.com")).ReturnsAsync(user);
        _usersRepo.Setup(r => r.IsValidPasswordAsync(user, "wrong")).ReturnsAsync(false);

        var result = await CreateSut().AuthenticateAsync("john@example.com", "wrong");

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Authenticate_WithValidCredentials_ReturnsSuccess()
    {
        var user = new User("john@example.com");
        var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
        _usersRepo.Setup(r => r.FindByEmailAsync("john@example.com")).ReturnsAsync(user);
        _usersRepo.Setup(r => r.IsValidPasswordAsync(user, "correct")).ReturnsAsync(true);
        _tokenManager.Setup(t => t.Generate(user)).Returns(token);

        var result = await CreateSut().AuthenticateAsync("john@example.com", "correct");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Authenticate_WithValidCredentials_ReturnsGeneratedToken()
    {
        var user = new User("john@example.com");
        var expected = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
        _usersRepo.Setup(r => r.FindByEmailAsync("john@example.com")).ReturnsAsync(user);
        _usersRepo.Setup(r => r.IsValidPasswordAsync(user, "correct")).ReturnsAsync(true);
        _tokenManager.Setup(t => t.Generate(user)).Returns(expected);

        var result = await CreateSut().AuthenticateAsync("john@example.com", "correct");

        Assert.Same(expected, result.Value);
    }

    [Fact]
    public async Task Authenticate_WithUnknownUser_NeverCallsTokenManager()
    {
        _usersRepo.Setup(r => r.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User)null!);

        await CreateSut().AuthenticateAsync("unknown@example.com", "password");

        _tokenManager.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_WithBadPassword_NeverCallsTokenManager()
    {
        var user = new User("john@example.com");
        _usersRepo.Setup(r => r.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        _usersRepo.Setup(r => r.IsValidPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(false);

        await CreateSut().AuthenticateAsync("john@example.com", "wrong");

        _tokenManager.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_StampsTheGuidFromRegisterLogin()
    {
        var uid = Guid.NewGuid();
        _webmailUsers.Setup(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync(uid);
        _usersRepo.Setup(r => r.FindByEmailAsync("mick@weesky.be")).ReturnsAsync(new User("mick@weesky.be"));
        _usersRepo.Setup(r => r.IsValidPasswordAsync(It.IsAny<User>(), "pw")).ReturnsAsync(true);

        var sut = new UserAuthenticator(_usersRepo.Object, RealTokenManager(), _webmailUsers.Object,
            Mock.Of<ILogger<UserAuthenticator>>());
        var result = await sut.AuthenticateAsync("mick@weesky.be", "pw");

        Assert.True(result.IsSuccess);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Value.Token);
        Assert.Equal(uid.ToString(), jwt.Claims.First(c => c.Type == WebmailClaimTypes.Uid).Value);
        _webmailUsers.Verify(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
    }
}
