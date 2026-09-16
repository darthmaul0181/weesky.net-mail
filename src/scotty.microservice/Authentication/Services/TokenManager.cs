using Microsoft.Extensions.Options;
using System.Security.Claims;
using weesky.Scotty.Microservice.Authentication.Extensions;
using weesky.Scotty.Microservice.Authentication.Models;
using weesky.Scotty.Microservice.Data;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Authentication.Services;

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
