using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace weesky.Snoopy.Microservice.Authentication.Services;

public sealed class TokenBuilder
{
    #region Members
    private string _issuer;
    private string _audience;
    private DateTime _expires;
    private SigningCredentials _credentials;
    private SymmetricSecurityKey _key;
    private List<Claim> _claims;
    #endregion

    public TokenBuilder()
    {
        _claims = new List<Claim>();
    }

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
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        _credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);

        return this;
    }

    public JwtSecurityToken Build()
    {
        return new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: _claims,
            expires: _expires,
            signingCredentials: _credentials
        );
    }
}
