using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Snoopy.Microservice.Authentication.Authorization;
using weesky.Snoopy.Providers.Weesky.Repositories;

namespace weesky.Snoopy.Providers.Weesky.Authorization;

/// <summary>
/// Runs on every request to an admin endpoint; the flag itself is cached by
/// <see cref="IAdminRepository.IsAdminAsync"/> for the same window a session check uses.
/// </summary>
public sealed class AdminRequirementHandler(IAdminRepository adminRepository)
    : AuthorizationHandler<AdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
        var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;

        // The authorization middleware hands the HttpContext through as the resource, which is the
        // only request-scoped token this handler ever sees. A policy evaluated outside a request
        // has no request to abandon, so None is the honest answer there.
        var cancellationToken = (context.Resource as HttpContext)?.RequestAborted ?? CancellationToken.None;

        if (!string.IsNullOrWhiteSpace(name) &&
            !string.IsNullOrWhiteSpace(domain) &&
            await adminRepository.IsAdminAsync(name, domain, cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
