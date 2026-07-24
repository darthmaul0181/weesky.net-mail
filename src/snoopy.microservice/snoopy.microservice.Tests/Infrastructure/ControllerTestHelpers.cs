using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using weesky.Snoopy.Microservice.Authentication;

namespace weesky.Snoopy.Microservice.Tests.Infrastructure;

internal static class ControllerTestHelpers
{
    public static ControllerContext CreateAuthenticatedContext(string username, string domain, Guid? webmailUid = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Upn, username),
            new(ClaimTypes.Dns, domain)
        };
        if (webmailUid.HasValue)
            claims.Add(new Claim(WebmailClaimTypes.Uid, webmailUid.Value.ToString()));
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }
}
