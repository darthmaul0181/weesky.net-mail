using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.IdentityModel.JsonWebTokens;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Models.Mail;
using weesky.Snoopy.Microservice.Repositories;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class UserAuthenticatorTests
{
    private readonly Mock<IImapConnectionFactory> _factory = new();
    private readonly Mock<IImapSession> _session = new();
    private readonly Mock<ITokenManager> _tokenManager = new();
    private readonly Mock<IWebmailUserStore> _webmailUsers = new();
    private readonly Mock<IOptionsMonitor<MailOptions>> _mail = new();

    public UserAuthenticatorTests()
    {
        _mail.Setup(m => m.CurrentValue).Returns(TestConnections.HomeOptions());
    }

    private UserAuthenticator CreateSut() =>
        new(_factory.Object, _mail.Object, _tokenManager.Object, _webmailUsers.Object,
            Mock.Of<ILogger<UserAuthenticator>>());

    private static TokenManager RealTokenManager() => new(Options.Create(new TokenConstants
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpiryInMinutes = 30,
        Key = "test-signing-key-long-enough-for-hmac256",
        AuthCookieName = "BearerAuth"
    }), TimeProvider.System);

    private void SetupImapSuccess(string email, string password) =>
        _factory.Setup(f => f.OpenAsync(TestConnections.Primary(email, password), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Success(_session.Object));

    private void SetupImapFailure(string email, string password, string error) =>
        _factory.Setup(f => f.OpenAsync(TestConnections.Primary(email, password), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result.Failure<IImapSession>(error));

    [Fact]
    public async Task Authenticate_ImapLoginSucceeds_GeneratesToken()
    {
        SetupImapSuccess("alice@weesky.be", "pw");
        _webmailUsers.Setup(s => s.RegisterLoginAsync("alice@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));
        var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token" };
        _tokenManager.Setup(t => t.Generate(It.IsAny<User>())).Returns(token);

        var result = await CreateSut().AuthenticateAsync("alice@weesky.be", "pw", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(token, result.Value);
        // The session is disposed by the authenticator: it never outlives the login call.
        _session.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Authenticate_ImapRefuses_OpaqueFailure()
    {
        SetupImapFailure("alice@weesky.be", "wrong", "AUTHENTICATIONFAILED");

        var result = await CreateSut().AuthenticateAsync("alice@weesky.be", "wrong", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Authentication failed", result.Error); // never the IMAP detail
    }

    [Fact]
    public async Task Authenticate_ServerUnreachable_SameOpaqueFailure()
    {
        SetupImapFailure("alice@weesky.be", "pw", "Could not connect to imap.home.test:143");

        var result = await CreateSut().AuthenticateAsync("alice@weesky.be", "pw", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Authentication failed", result.Error); // indistinguishable from a bad password
    }

    [Fact]
    public async Task Authenticate_Failure_NeverTouchesWebmailStore()
    {
        SetupImapFailure("alice@weesky.be", "wrong", "AUTHENTICATIONFAILED");

        await CreateSut().AuthenticateAsync("alice@weesky.be", "wrong", CancellationToken.None);

        _webmailUsers.Verify(
            s => s.RegisterLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokenManager.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Authenticate_StampsTheGuidFromRegisterLogin()
    {
        var uid = Guid.NewGuid();
        SetupImapSuccess("mick@weesky.be", "pw");
        _webmailUsers.Setup(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(uid, Guid.NewGuid()));

        var sut = new UserAuthenticator(_factory.Object, _mail.Object, RealTokenManager(), _webmailUsers.Object,
            Mock.Of<ILogger<UserAuthenticator>>());
        var result = await sut.AuthenticateAsync("mick@weesky.be", "pw", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var jwt = new JsonWebToken(result.Value.Token);
        Assert.Equal(uid.ToString(), jwt.Claims.First(c => c.Type == WebmailClaimTypes.Uid).Value);
        _webmailUsers.Verify(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
    }

    // Both effects of a successful login are the caller's to abandon: the IMAP session opened
    // to prove the password, and the upsert that precedes the cookies and whose id/stamp the
    // token is built from.
    [Fact]
    public async Task Authenticate_ForwardsItsTokenToBothStores()
    {
        using var cts = new CancellationTokenSource();
        SetupImapSuccess("mick@weesky.be", "pw");
        _webmailUsers.Setup(s => s.RegisterLoginAsync("mick@weesky.be", cts.Token))
            .ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));

        var sut = new UserAuthenticator(_factory.Object, _mail.Object, RealTokenManager(), _webmailUsers.Object,
            Mock.Of<ILogger<UserAuthenticator>>());
        var result = await sut.AuthenticateAsync("mick@weesky.be", "pw", cts.Token);

        Assert.True(result.IsSuccess);
        _factory.Verify(f => f.OpenAsync(TestConnections.Primary("mick@weesky.be", "pw"), cts.Token), Times.Once);
        _webmailUsers.Verify(s => s.RegisterLoginAsync("mick@weesky.be", cts.Token), Times.Once);
    }

    // RegisterLoginAsync and the JWT's Upn claim must key on the same spelling the caller typed,
    // trimmed/lowercased — never on whatever case or whitespace the login form submitted.
    [Fact]
    public async Task Authenticate_CanonicalisesTheEmailBeforeRegisteringTheLogin()
    {
        SetupImapSuccess("mick@weesky.be", "pw");
        _webmailUsers.Setup(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebmailAccount(Guid.NewGuid(), Guid.NewGuid()));
        _tokenManager.Setup(t => t.Generate(It.IsAny<User>()))
            .Returns(new AuthToken { ExpiresIn = 30, Token = "jwt.token" });

        var result = await CreateSut().AuthenticateAsync("  Mick@Weesky.BE  ", "pw", CancellationToken.None);

        Assert.True(result.IsSuccess);
        _webmailUsers.Verify(s => s.RegisterLoginAsync("mick@weesky.be", It.IsAny<CancellationToken>()), Times.Once);
    }
}
