using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace weesky.Snoopy.Microservice.Authentication.Services;

/// <summary>
/// Builds the signed JWT the API authenticates with.
///
/// On <see cref="JsonWebTokenHandler"/>, not the legacy <c>JwtSecurityTokenHandler</c>: the bearer
/// middleware already validates with the former, so writing with the latter left two JWT stacks in
/// one service. Claim types go out verbatim — neither handler rewrites them here — so the Upn/Dns
/// claims the token carries are the ones validation looks for.
/// </summary>
public sealed class TokenBuilder
{
    private readonly List<Claim> _claims = [];
    private string? _issuer;
    private string? _audience;
    private DateTime? _expires;
    private SigningCredentials? _credentials;

    public TokenBuilder AddClaims(params Claim[] claims)
    {
        _claims.AddRange(claims);
        return this;
    }

    public TokenBuilder AddClaim(Claim claim)
    {
        _claims.Add(claim);
        return this;
    }

    public TokenBuilder AddClaim(string type, string value)
    {
        _claims.Add(new Claim(type, value));
        return this;
    }

    public TokenBuilder AddIssuer(string issuer)
    {
        _issuer = issuer;
        return this;
    }

    public TokenBuilder AddAudience(string audience)
    {
        _audience = audience;
        return this;
    }

    public TokenBuilder AddExpiry(int minutes)
    {
        _expires = DateTime.UtcNow.AddMinutes(minutes);
        return this;
    }

    public TokenBuilder AddKey(string key)
    {
        var material = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        _credentials = new SigningCredentials(material, SecurityAlgorithms.HmacSha256);

        return this;
    }

    /// <summary>The serialized token. This handler emits "iat" and "nbf" alongside "exp".</summary>
    public string Build()
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            Subject = new ClaimsIdentity(_claims),
            SigningCredentials = _credentials
        };

        if (_expires is { } expires) descriptor.Expires = expires;

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
