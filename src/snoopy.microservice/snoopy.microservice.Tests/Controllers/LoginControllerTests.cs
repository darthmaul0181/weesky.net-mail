using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Controllers;
using weesky.Snoopy.Microservice.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;
using weesky.Snoopy.Microservice.Tests.Infrastructure;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Controllers;

public sealed class LoginControllerTests
{
    private static readonly TokenConstants TestTokenConstants = new()
    {
        Issuer = "issuer",
        Audience = "audience",
        ExpiryInMinutes = 30,
        Key = "signing-key-long-enough-for-hmac256",
        AuthCookieName = "BearerAuth"
    };

    // Derived once for the whole class: 600k PBKDF2 iterations are not free.
    private static readonly byte[] TestSalt = ConnectedAccountCipher.NewSalt();
    private static readonly byte[] ExpectedKek = ConnectedAccountCipher.DeriveKek("hunter2", TestSalt);

    private readonly Mock<IUserAuthenticator> _authenticator = new();
    private readonly Mock<IMailCredentialStore> _credentialStore = new();
    private readonly Mock<IWebmailUserStore> _webmailUsers = new();
    private readonly Mock<ISessionGuard> _sessions = new();
    private readonly Mock<IImapConnectionPool> _pool = new();

    public LoginControllerTests()
        => _webmailUsers.Setup(s => s.GetOrCreateKdfSaltAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(TestSalt);

    private LoginController CreateController(DefaultHttpContext? httpContext = null)
    {
        var controller = new LoginController(
            _authenticator.Object, Options.Create(TestTokenConstants), _credentialStore.Object,
            _webmailUsers.Object, _sessions.Object, _pool.Object, NullLogger<LoginController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext ?? new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200WithTheExpiryOnly()
    {
        var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token", Email = "user@domain.com" };
        _authenticator.Setup(a => a.AuthenticateAsync("user@domain.com", "pass", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(token));

        var result = await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "pass" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<LoginResponse>(ok.Value);
        Assert.Equal(30, body.ExpiresIn);
    }

    // The JWT lives in an HttpOnly cookie; handing the same string to page scripts, devtools and
    // every intermediary log would give that flag away for nothing.
    [Fact]
    public async Task Login_DoesNotSerialiseTheJwtIntoTheResponseBody()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AuthToken { ExpiresIn = 30, Token = "jwt.token", Email = "user@domain.com" }));

        var result = await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "pass" }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var json = JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("jwt.token", json);
        Assert.Contains("\"expiresIn\":30", json);
    }

    [Fact]
    public async Task Login_WithValidCredentials_WritesTheJwtIntoTheAuthCookie()
    {
        var token = new AuthToken { ExpiresIn = 30, Token = "jwt.token", Email = "user@domain.com" };
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(token));
        var httpContext = new DefaultHttpContext();

        await CreateController(httpContext).Login(new Credentials { Email = "user@domain.com", Password = "pass" }, CancellationToken.None);

        Assert.Contains("BearerAuth=jwt.token", string.Join(";", httpContext.Response.Headers["Set-Cookie"].ToArray()));
    }

    // Carried over from BearerAuthenticatorControllerTests when that endpoint was retired: the
    // credentials must reach the authenticator unaltered, and nothing else asserted it outright.
    [Fact]
    public async Task Login_PassesEmailAndPasswordToTheAuthenticator()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthToken>("Authentication failed"));

        await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "pass" }, CancellationToken.None);

        _authenticator.Verify(a => a.AuthenticateAsync("user@domain.com", "pass", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthToken>("Authentication failed"));

        var result = await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "wrong" }, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(401, obj.StatusCode);
        var envelope = Assert.IsType<ResultEnveloppe>(obj.Value);
        Assert.Equal("Authentication failed", envelope.Message);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_DoesNotSetCookie()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthToken>("Authentication failed"));
        var httpContext = new DefaultHttpContext();

        await CreateController(httpContext).Login(new Credentials { Email = "user@domain.com", Password = "wrong" }, CancellationToken.None);

        Assert.False(httpContext.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public void Logout_Returns204()
    {
        var controller = new LoginController(
            _authenticator.Object, Options.Create(TestTokenConstants), _credentialStore.Object,
            _webmailUsers.Object, _sessions.Object, _pool.Object, NullLogger<LoginController>.Instance);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");

        var result = controller.Logout();

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Login_OnSuccess_StoresTheCredentialsCookie()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AuthToken { ExpiresIn = 30, Token = "jwt.token", Email = "user@domain.com" }));

        await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "hunter2" }, CancellationToken.None);

        _credentialStore.Verify(
            s => s.Store(It.IsAny<HttpResponse>(), It.Is<MailCredentialPayload>(p => p.Password == "hunter2"),
                TimeSpan.FromMinutes(30)),
            Times.Once);
    }

    // The KEK costs 600k PBKDF2 iterations, far too much to pay per request, so login is the one
    // moment it is derived — the cookie carries it for the rest of the session.
    [Fact]
    public async Task Login_StoresTheKekAlongsideThePassword()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AuthToken { ExpiresIn = 30, Token = "jwt.token", Email = "user@domain.com" }));
        MailCredentialPayload? stored = null;
        _credentialStore.Setup(s => s.Store(It.IsAny<HttpResponse>(), It.IsAny<MailCredentialPayload>(), It.IsAny<TimeSpan>()))
                        .Callback<HttpResponse, MailCredentialPayload, TimeSpan>((_, p, _) => stored = p);

        await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "hunter2" }, CancellationToken.None);

        _webmailUsers.Verify(s => s.GetOrCreateKdfSaltAsync("user@domain.com", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(stored);
        Assert.Equal("hunter2", stored.Password);
        Assert.Equal<byte[]>(ExpectedKek, stored.Kek!);
    }

    // The salt row belongs to the account the credentials resolved to, not to the address that was
    // typed. Reading it under the caller's spelling can miss the row and hand back a salt nobody
    // persisted — a key that opens none of the connected accounts, and only says so much later.
    [Fact]
    public async Task Login_FetchesTheSaltUnderTheResolvedAddress_NotTheOneTyped()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(
                new AuthToken { ExpiresIn = 30, Token = "jwt.token", Email = "user@domain.com" }));

        await CreateController().Login(
            new Credentials { Email = " User@Domain.com ", Password = "hunter2" }, CancellationToken.None);

        _webmailUsers.Verify(s => s.GetOrCreateKdfSaltAsync("user@domain.com", It.IsAny<CancellationToken>()), Times.Once);
        _webmailUsers.Verify(s => s.GetOrCreateKdfSaltAsync(" User@Domain.com ", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_OnFailure_DoesNotStoreTheCredentialsCookie()
    {
        _authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<AuthToken>("Invalid credentials"));

        await CreateController().Login(new Credentials { Email = "user@domain.com", Password = "wrong" }, CancellationToken.None);

        _credentialStore.Verify(
            s => s.Store(It.IsAny<HttpResponse>(), It.IsAny<MailCredentialPayload>(), It.IsAny<TimeSpan>()),
            Times.Never);
    }

    [Fact]
    public void Logout_ClearsTheCredentialsCookie()
    {
        var controller = new LoginController(
            _authenticator.Object, Options.Create(TestTokenConstants), _credentialStore.Object,
            _webmailUsers.Object, _sessions.Object, _pool.Object, NullLogger<LoginController>.Instance);
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");

        controller.Logout();

        _credentialStore.Verify(s => s.Clear(It.IsAny<HttpResponse>()), Times.Once);
    }

    // The ordinary logout only clears this browser's cookies; a copy taken off the machine keeps
    // working until the token expires. This is the control that actually cuts it.
    [Fact]
    public async Task LogoutEverywhere_RotatesTheStampAndDropsTheCachedState()
    {
        var controller = CreateController();
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com");

        var result = await controller.LogoutEverywhere(CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        _webmailUsers.Verify(s => s.RotateSecurityStampAsync("john@example.com", It.IsAny<CancellationToken>()), Times.Once);
        _sessions.Verify(s => s.Forget("john@example.com"), Times.Once);
        _credentialStore.Verify(c => c.Clear(It.IsAny<HttpResponse>()), Times.Once);
    }

    // DELETE /Login is housekeeping: the user's idle sockets go, the generation does not turn.
    [Fact]
    public void Logout_ClosesTheUsersPooledSockets()
    {
        var uid = Guid.NewGuid();
        var controller = CreateController();
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", uid);

        controller.Logout();

        _pool.Verify(p => p.Close(uid), Times.Once);
        _pool.Verify(p => p.Revoke(It.IsAny<Guid>()), Times.Never);
    }

    // DELETE /Login/All is the revocation: sockets out right now must not come back either.
    [Fact]
    public async Task LogoutEverywhere_RevokesTheUsersPooledSockets()
    {
        var uid = Guid.NewGuid();
        var controller = CreateController();
        controller.ControllerContext = ControllerTestHelpers.CreateAuthenticatedContext("john", "example.com", uid);

        await controller.LogoutEverywhere(CancellationToken.None);

        _pool.Verify(p => p.Revoke(uid), Times.Once);
    }
}
