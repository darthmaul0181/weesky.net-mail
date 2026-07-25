using System.IdentityModel.Tokens.Jwt;

namespace weesky.Snoopy.Microservice.Authentication.Extensions;

public static class JwtSecurityTokenExtensions
{
    public static string SerializeToString(this JwtSecurityToken token) => new JwtSecurityTokenHandler().WriteToken(token);
}
