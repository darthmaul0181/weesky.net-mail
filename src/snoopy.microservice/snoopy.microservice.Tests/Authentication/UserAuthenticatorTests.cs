using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.IdentityModel.JsonWebTokens;
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

    private void SetupCheck(CredentialCheck check) =>
        _usersRepo.Setup(r => r.VerifyCredentialsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(check);

    private UserAuthenticator CreateSut() =>
        new(_usersRepo.Object, _tokenManager.Object, _webmailUsers.Object, Mock.Of<ILogger<UserAuthenticator>>());

    private static TokenManager RealTokenManager() => new(Options.Create(new TokenConstants
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpiryInMinutes = 30,
        Key = "test-signing-key-long-enough-for-hmac256",
        AuthCookieName = "BearerAuth"
    }), TimeProvider.System);

    [Fact]
    public async Task Authenticate_WithUnknownUser_ReturnsFailure()
    {
        SetupCheck(CredentialCheck.Failed(CredentialResult.UnknownAccount));

        var result = await CreateSut().AuthenticateAsync("unknown@example.com", "password", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Authenticate_WithBadPassword_ReturnsFailure()
    {
        SetupCheck(CredentialCheck.Failed(CredentialResult.WrongPassword));

        var result = await CreateSut().AuthenticateAsync("john@example.com", "wrong", CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Authenticate_WithValidCredentials_ReturnsSuccess()
    {
        var user = new User("john@example.com");
        var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
        SetupCheck(CredentialCheck.Success(user));
        _tokenManager.Setup(t => t.Generate(user)).Returns(token);

        var result = await CreateSut().AuthenticateAsync("john@example.com", "correct", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Authenticate_WithValidCredentials_ReturnsGeneratedToken()
    {
        var user = new User("john@example.com");
        var expected = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
        SetupCheck(CredentialCheck.Success(user));
        _tokenManager.Setup(t => t.Generate(user)).Returns(expected);

        var result = await CreateSut().AuthenticateAsync("john@example.com", "correct", CancellationToken.None);

        Assert.Same(expected, result.Value);
    }

    [Fact]
    public async Task Authenticate_WithUnknownUser_NeverCallsTokenManager()
    {
        SetupCheck(CredentialCheck.Failed(CredentialResult.UnknownAccount));

        await CreateSut().AuthenticateAsync("unknown@example.com", "password", CancellationToken.None);

        _tokenManager.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_WithBadPassword_NeverCallsTokenManager()
    {
        SetupCheck(CredentialCheck.Failed(CredentialResult.WrongPassword));

        await CreateSut().AuthenticateAsync("john@example.com", "wrong", CancellationToken.None);

        _tokenManager.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    // These lines get grepped: the wording is an interface, not an implementation detail.
    [Theory]
    [InlineData(CredentialResult.UnknownAccount, "unknown_account")]
    [InlineData(CredentialResult.Deactivated, "deactivated")]
    [InlineData(CredentialResult.WrongPassword, "bad_password")]
    public void AuditReason_IsAStableSnakeCaseToken(CredentialResult result, string expected)
    {
        Assert.Equal(expected, UserAuthenticator.AuditReason(result));
    }

    [Fact]
    public async Task Authenticate_StampsTheGuidFromRegisterLogin()
    {
        var uid = Guid.NewGuid();
        _webmailUsers.Setup(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(uid, Guid.NewGuid()));
        SetupCheck(CredentialCheck.Success(new User("mick@weesky.be")));

        var sut = new UserAuthenticator(_usersRepo.Object, RealTokenManager(), _webmailUsers.Object,
            Mock.Of<ILogger<UserAuthenticator>>());
        var result = await sut.AuthenticateAsync("mick@weesky.be", "pw", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var jwt = new JsonWebToken(result.Value.Token);
        Assert.Equal(uid.ToString(), jwt.Claims.First(c => c.Type == WebmailClaimTypes.Uid).Value);
        _webmailUsers.Verify(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
    }

    // Both database calls of a login are the caller's to abandon: the credential check, and the
    // upsert that precedes the cookies and whose id and stamp the token is built from.
    [Fact]
    public async Task Authenticate_ForwardsItsTokenToBothStores()
    {
        using var cts = new CancellationTokenSource();
        _webmailUsers.Setup(s => s.RegisterLoginAsync("mick@weesky.be", cts.Token))
            .ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));
        _usersRepo.Setup(r => r.VerifyCredentialsAsync("mick@weesky.be", "pw", cts.Token))
            .ReturnsAsync(CredentialCheck.Success(new User("mick@weesky.be")));

        var sut = new UserAuthenticator(_usersRepo.Object, RealTokenManager(), _webmailUsers.Object,
            Mock.Of<ILogger<UserAuthenticator>>());
        var result = await sut.AuthenticateAsync("mick@weesky.be", "pw", cts.Token);

        Assert.True(result.IsSuccess);
        _usersRepo.Verify(r => r.VerifyCredentialsAsync("mick@weesky.be", "pw", cts.Token), Times.Once);
        _webmailUsers.Verify(s => s.RegisterLoginAsync("mick@weesky.be", cts.Token), Times.Once);
    }
}
