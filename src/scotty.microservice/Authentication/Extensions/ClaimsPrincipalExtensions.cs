using System.Security.Claims;

namespace weesky.Scotty.Microservice.Authentication.Extensions;

internal static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's whole address, recombined from the Upn (local part) and Dns claims; null without both.</summary>
    public static string? MailboxOf(this ClaimsPrincipal? principal)
    {
        var name = principal?.FindFirst(ClaimTypes.Upn)?.Value;
        var domain = principal?.FindFirst(ClaimTypes.Dns)?.Value;
        return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(domain) ? null : $"{name}@{domain}";
    }
}
