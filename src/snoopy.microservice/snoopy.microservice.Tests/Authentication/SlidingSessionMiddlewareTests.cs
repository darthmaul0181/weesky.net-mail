using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;
using weesky.Snoopy.Microservice.Authentication.Middleware;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class SlidingSessionMiddlewareTests
{
    private readonly Mock<ITokenManager> _tokens = new();
    private readonly Mock<IMailCredentialStore> _credentials = new();

    private readonly TokenConstants _constants = new()
    {
        Issuer = "issuer",
        Audience = "audience",
        ExpiryInMinutes = 30,
        Key = "signing-key-long-enough-for-hmac256",
        AuthCookieName = "BearerAuth"
    };

    public SlidingSessionMiddlewareTests()
    {
        _tokens.Setup(t => t.Generate(It.IsAny<User>())).Returns(new AuthToken { ExpiresIn = 30, Token = "fresh" });
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>())).Returns(Result.Success("hunter2"));
    }

    /// <summary>
    /// Builds a principal carrying an "exp" claim, which is what the middleware reads.
    /// The token builder does not emit "iat", so remaining lifetime — not age — is the
    /// only thing the middleware can measure.
    /// </summary>
    private static HttpContext CreateContext(bool authenticated, TimeSpan remaining)
    {
        var context = new DefaultHttpContext();

        if (authenticated)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Upn, "alice"),
                new Claim(ClaimTypes.Dns, "weesky.be"),
                new Claim(JwtRegisteredClaimNames.Exp,
                    DateTimeOffset.UtcNow.Add(remaining).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        return context;
    }

    private SlidingSessionMiddleware CreateSut(RequestDelegate? next = null)
        => new(next ?? (_ => Task.CompletedTask), Options.Create(_constants));

    [Fact]
    public async Task Invoke_RenewsBothCookiesPastHalfLife()
    {
        // 10 minutes left of a 30-minute lifetime: past the halfway mark.
        var context = CreateContext(authenticated: true, remaining: TimeSpan.FromMinutes(10));

        await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

        Assert.Contains("BearerAuth=fresh", string.Join(";", context.Response.Headers["Set-Cookie"].ToArray()));
        _credentials.Verify(c => c.Store(It.IsAny<HttpResponse>(), "hunter2", TimeSpan.FromMinutes(30)), Times.Once);
    }

    [Fact]
    public async Task Invoke_RenewsForTheAuthenticatedUser()
    {
        var context = CreateContext(authenticated: true, remaining: TimeSpan.FromMinutes(10));

        await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

        _tokens.Verify(t => t.Generate(It.Is<User>(u => u.Email == "alice@weesky.be")), Times.Once);
    }

    [Fact]
    public async Task Invoke_DoesNotRenewBeforeHalfLife()
    {
        // 25 minutes left of 30: well before the halfway mark.
        var context = CreateContext(authenticated: true, remaining: TimeSpan.FromMinutes(25));

        await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

        _tokens.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
        _credentials.Verify(c => c.Store(It.IsAny<HttpResponse>(), It.IsAny<string>(), It.IsAny<TimeSpan>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_DoesNothingForAnUnauthenticatedRequest()
    {
        var context = CreateContext(authenticated: false, remaining: TimeSpan.FromMinutes(10));

        await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

        _tokens.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_DoesNotRenewWhenCredentialsAreGone()
    {
        _credentials.Setup(c => c.Retrieve(It.IsAny<HttpRequest>()))
                    .Returns(Result.Failure<string>("credentials_unavailable"));
        var context = CreateContext(authenticated: true, remaining: TimeSpan.FromMinutes(10));

        await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

        // Renewing the JWT alone would leave a session that looks alive but cannot open IMAP.
        _tokens.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_DoesNothingWithoutAnExpClaim()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Upn, "alice"), new Claim(ClaimTypes.Dns, "weesky.be") }, "Test"));

        await CreateSut().InvokeAsync(context, _tokens.Object, _credentials.Object);

        _tokens.Verify(t => t.Generate(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task Invoke_AlwaysCallsTheNextMiddleware()
    {
        var called = false;
        var sut = CreateSut(_ => { called = true; return Task.CompletedTask; });

        await sut.InvokeAsync(
            CreateContext(authenticated: false, remaining: TimeSpan.FromMinutes(10)),
            _tokens.Object, _credentials.Object);

        Assert.True(called);
    }

    [Fact]
    public async Task Invoke_CallsTheNextMiddlewareEvenAfterRenewing()
    {
        var called = false;
        var sut = CreateSut(_ => { called = true; return Task.CompletedTask; });

        await sut.InvokeAsync(
            CreateContext(authenticated: true, remaining: TimeSpan.FromMinutes(10)),
            _tokens.Object, _credentials.Object);

        Assert.True(called);
    }
}
