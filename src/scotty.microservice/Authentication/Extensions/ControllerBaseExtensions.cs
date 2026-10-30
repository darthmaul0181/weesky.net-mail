using Microsoft.AspNetCore.Mvc;
using weesky.Scotty.Microservice.Models;

namespace weesky.Scotty.Microservice.Authentication.Extensions;

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
        var principal = controller.HttpContext?.User;
        if (principal.MailboxOf() is not { } mailbox) return null;

        var user = new User(mailbox);
        if (Guid.TryParse(principal!.FindFirst(WebmailClaimTypes.Uid)?.Value, out var uid))
            user.WebmailUid = uid;

        return user;
    }
}
