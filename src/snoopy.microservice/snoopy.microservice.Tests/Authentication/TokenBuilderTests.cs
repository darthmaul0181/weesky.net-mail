using Microsoft.IdentityModel.Tokens;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication.Services;
using Xunit;

namespace weesky.Snoopy.Microservice.Tests.Authentication;

public sealed class TokenBuilderTests
{
    private const string TestKey = "test-signing-key-long-enough-for-hmac256-hashing";

    [Fact]
    public void Build_WithAllProperties_ReturnsAReadableSignedToken()
    {
        var token = new JsonWebToken(new TokenBuilder()
            .AddClaim(ClaimTypes.Upn, "john")
            .AddIssuer("issuer")
            .AddAudience("audience")
            .AddExpiry(30, DateTime.UtcNow)
            .AddKey(TestKey)
            .Build());

        Assert.NotNull(token);
        Assert.Equal(3, token.EncodedToken.Split('.').Length);
    }

    [Fact]
    public void AddClaim_StringTypeAndValue_AddsClaimToToken()
    {
        var token = new JsonWebToken(new TokenBuilder()
            .AddClaim("mytype", "myvalue")
            .AddKey(TestKey)
            .Build());

        Assert.Contains(token.Claims, c => c.Type == "mytype" && c.Value == "myvalue");
    }

    [Fact]
    public void AddIssuer_SetsIssuerOnToken()
    {
        var token = new JsonWebToken(new TokenBuilder()
            .AddIssuer("my-issuer")
            .AddKey(TestKey)
            .Build());

        Assert.Equal("my-issuer", token.Issuer);
    }

    [Fact]
    public void AddExpiry_SetsExpiration()
    {
        var token = new JsonWebToken(new TokenBuilder()
            .AddExpiry(30, DateTime.UtcNow)
            .AddKey(TestKey)
            .Build());

        var minutesToExpiry = (token.ValidTo - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutesToExpiry, 29.9, 30.1);
    }

    [Fact]
    public void AddExpiry_IsDrivenByTheGivenInstant_NotTheWallClock()
    {
        var utcNow = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var token = new JsonWebToken(new TokenBuilder()
            .AddExpiry(30, utcNow)
            .AddKey(TestKey)
            .Build());

        Assert.Equal(utcNow.AddMinutes(30), token.ValidTo);
    }

    [Fact]
    public void AddKey_UsesHmacSha256Algorithm()
    {
        var token = new JsonWebToken(new TokenBuilder()
            .AddKey(TestKey)
            .Build());

        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Alg);
    }

    [Fact]
    public void FluentApi_AllMethodsReturnBuilderInstance()
    {
        var builder = new TokenBuilder();

        var result = builder
            .AddClaim("t", "v")
            .AddIssuer("i")
            .AddAudience("a")
            .AddExpiry(10, DateTime.UtcNow)
            .AddKey(TestKey);

        Assert.Same(builder, result);
    }
}
