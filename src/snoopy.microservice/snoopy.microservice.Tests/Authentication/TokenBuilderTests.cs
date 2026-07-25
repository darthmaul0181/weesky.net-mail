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
            .AddExpiry(30)
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
    public void AddClaim_ClaimObject_AddsClaimToToken()
    {
        var token = new JsonWebToken(new TokenBuilder()
            .AddClaim(new Claim("mytype", "myvalue"))
            .AddKey(TestKey)
            .Build());

        Assert.Contains(token.Claims, c => c.Type == "mytype" && c.Value == "myvalue");
    }

    [Fact]
    public void AddClaims_Array_AddsAllClaimsToToken()
    {
        var token = new JsonWebToken(new TokenBuilder()
            .AddClaims(new Claim("type1", "val1"), new Claim("type2", "val2"))
            .AddKey(TestKey)
            .Build());

        Assert.Contains(token.Claims, c => c.Type == "type1" && c.Value == "val1");
        Assert.Contains(token.Claims, c => c.Type == "type2" && c.Value == "val2");
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
            .AddExpiry(30)
            .AddKey(TestKey)
            .Build());

        var minutesToExpiry = (token.ValidTo - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutesToExpiry, 29.9, 30.1);
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
            .AddExpiry(10)
            .AddKey(TestKey);

        Assert.Same(builder, result);
    }
}
