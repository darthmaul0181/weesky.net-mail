using Microsoft.IdentityModel.JsonWebTokens;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using weesky.Snoopy.Microservice.Authentication.Models;
using weesky.Snoopy.Microservice.Authentication.Services;
using weesky.Snoopy.Microservice.Models;
using weesky.Snoopy.Microservice.Services;

namespace weesky.Snoopy.Microservice.Authentication.Middleware;

/// <summary>
/// Extends a live session in place rather than letting it expire mid-use. A webmail stays
/// open for hours; a 30-minute token with no renewal would sign the user out while they
/// read. This achieves that without a refresh token, an extra endpoint or a token store.
///
/// Both cookies are renewed together, or neither: renewing the JWT alone would leave a
/// session that looks alive but can no longer open IMAP.
/// </summary>
public sealed class SlidingSessionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptions<TokenConstants> _tokenConstants;

    public SlidingSessionMiddleware(RequestDelegate next, IOptions<TokenConstants> tokenConstants)
    {
        _next = next;
        _tokenConstants = tokenConstants;
    }

    public async Task InvokeAsync(
        HttpContext context, ITokenManager tokens, IMailCredentialStore credentials,
        ILogger<SlidingSessionMiddleware> logger)
    {
        TryRenew(context, tokens, credentials, logger);
        await _next(context);
    }

    private void TryRenew(
        HttpContext context, ITokenManager tokens, IMailCredentialStore credentials,
        ILogger<SlidingSessionMiddleware> logger)
    {
        if (context.User?.Identity?.IsAuthenticated != true) return;

        var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
        var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain)) return;

        // Remaining lifetime, measured on "exp". JsonWebTokenHandler does emit "iat" too, but
        // "exp" is what the renewal threshold is expressed against and needs no second reading.
        var expClaim = context.User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (!long.TryParse(expClaim, out var expiryUnix)) return;

        var lifetime = TimeSpan.FromMinutes(_tokenConstants.Value.ExpiryInMinutes);
        var remaining = DateTimeOffset.FromUnixTimeSeconds(expiryUnix) - DateTimeOffset.UtcNow;
        if (remaining > lifetime / 2) return;

        // Renew both or neither.
        var password = credentials.Retrieve(context.Request);
        if (password.IsFailure) return;

        var renewed = new User($"{name}@{domain}");
        if (Guid.TryParse(context.User.FindFirst(WebmailClaimTypes.Uid)?.Value, out var uid))
            renewed.WebmailUid = uid;
        var token = tokens.Generate(renewed);
        if (string.IsNullOrEmpty(token.Token)) return;

        context.Response.Cookies.Append(_tokenConstants.Value.AuthCookieName, token.Token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.Add(lifetime)
        });

        credentials.Store(context.Response, password.Value, lifetime);
        logger.LogInformation("Sliding session renewed for {User}: {RemainingMinutes} min remained of {LifetimeMinutes}",
            renewed.Email, (int)remaining.TotalMinutes, (int)lifetime.TotalMinutes);
    }
}
