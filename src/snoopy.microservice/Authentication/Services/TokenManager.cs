using Microsoft.Extensions.Options;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication.Extensions;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Data;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Authentication.Services;

public sealed class TokenManager(IOptions<TokenConstants> tokenConstants, TimeProvider timeProvider) : ITokenManager
{
    public AuthToken Generate(User user)
    {
        var constants = tokenConstants.Value;
        var tokenBuilder = new TokenBuilder();

        string token = tokenBuilder.AddClaim(ClaimTypes.Upn, user.Name)
            .AddClaim(ClaimTypes.Dns, user.Domain)
            .AddClaim(WebmailClaimTypes.Uid, user.WebmailUid.ToString())
            .AddClaim(WebmailClaimTypes.Stamp, user.SecurityStamp.ToString())
            .AddIssuer(constants.Issuer)
            .AddAudience(constants.Audience)
            .AddExpiry(constants.ExpiryInMinutes, timeProvider.GetUtcNow().UtcDateTime)
            .AddKey(constants.Key)
            .Build();

        return new AuthToken
        {
            ExpiresIn = constants.ExpiryInMinutes,
            Token = token,
            Email = user.Email
        };
    }
}
