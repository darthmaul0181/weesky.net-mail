using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class TokenManagerTests
{
    private static readonly TokenConstants Constants = new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        ExpiryInMinutes = 30,
        Key = "test-signing-key-long-enough-for-hmac256",
        AuthCookieName = "BearerAuth"
    };

    private static TokenManager CreateSut(TimeProvider? timeProvider = null) =>
        new(Options.Create(Constants), timeProvider ?? TimeProvider.System);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Generate_ReturnsAuthTokenWithCorrectExpiresIn()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        Assert.Equal(30, result.ExpiresIn);
    }

    [Fact]
    public void Generate_ReturnsNonEmptyToken()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        Assert.NotNull(result.Token);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public void Generate_TokenContainsUpnClaim()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        var jwt = new JsonWebToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Upn && c.Value == "john");
    }

    [Fact]
    public void Generate_TokenContainsDnsClaim()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        var jwt = new JsonWebToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Dns && c.Value == "example.com");
    }

    [Fact]
    public void Generate_TokenHasCorrectIssuer()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        var jwt = new JsonWebToken(result.Token);
        Assert.Equal("test-issuer", jwt.Issuer);
    }

    [Fact]
    public void Generate_TokenHasCorrectAudience()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        var jwt = new JsonWebToken(result.Token);
        Assert.Contains("test-audience", jwt.Audiences);
    }

    [Fact]
    public void Generate_StampsTheWebmailUidClaim()
    {
        var uid = Guid.NewGuid();
        var user = new User("mick@weesky.be") { WebmailUid = uid };
        var token = CreateSut().Generate(user);

        var jwt = new JsonWebToken(token.Token);
        Assert.Equal(uid.ToString(), jwt.Claims.First(c => c.Type == WebmailClaimTypes.Uid).Value);
    }

    [Fact]
    public void Generate_UsesInjectedTimeProvider_ExpiryMovesWithTheClock()
    {
        var fixedNow = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var sut = CreateSut(new FixedTimeProvider(fixedNow));

        var token = sut.Generate(new User("john@example.com"));

        var jwt = new JsonWebToken(token.Token);
        Assert.Equal(fixedNow.UtcDateTime.AddMinutes(Constants.ExpiryInMinutes), jwt.ValidTo);
    }

    [Fact]
    public void Generate_WithADifferentClockInstant_ProducesADifferentExpiry()
    {
        var earlier = CreateSut(new FixedTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero)))
            .Generate(new User("john@example.com"));
        var later = CreateSut(new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 12, 0, 0, TimeSpan.Zero)))
            .Generate(new User("john@example.com"));

        var earlierExpiry = new JsonWebToken(earlier.Token).ValidTo;
        var laterExpiry = new JsonWebToken(later.Token).ValidTo;

        Assert.Equal(TimeSpan.FromDays(1), laterExpiry - earlierExpiry);
    }
}
