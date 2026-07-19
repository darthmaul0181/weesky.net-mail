using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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

    private static TokenManager CreateSut() => new(Options.Create(Constants));

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

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Upn && c.Value == "john");
    }

    [Fact]
    public void Generate_TokenContainsDnsClaim()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Dns && c.Value == "example.com");
    }

    [Fact]
    public void Generate_TokenHasCorrectIssuer()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal("test-issuer", jwt.Issuer);
    }

    [Fact]
    public void Generate_TokenHasCorrectAudience()
    {
        var result = CreateSut().Generate(new User("john@example.com"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Contains("test-audience", jwt.Audiences);
    }
}
