using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Models;

namespace weesky.Snoopy.Microservice.Authentication.Extensions;

public static class ControllerBaseExtensions
{
    /// <summary>
    /// The caller as the JWT describes them, or null when the Upn/Dns claims are missing.
    ///
    /// Null is unreachable on an <c>[Authorize]</c>d action — the bearer handler fails a token
    /// carrying neither claim (see AuthorizationExtension) — so the nullable return describes the
    /// unauthenticated caller, not a case every action has to handle.
    /// </summary>
    public static User? GetUser(this ControllerBase controller)
    {
        IEnumerable<Claim> claims = controller.HttpContext?.User?.Claims ?? [];

        string? name = claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Upn)?.Value;
        string? domain = claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Dns)?.Value;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain)) return null;

        var user = new User($"{name}@{domain}");
        if (Guid.TryParse(claims.FirstOrDefault(c => c.Type == WebmailClaimTypes.Uid)?.Value, out var uid))
            user.WebmailUid = uid;

        return user;
    }
}
