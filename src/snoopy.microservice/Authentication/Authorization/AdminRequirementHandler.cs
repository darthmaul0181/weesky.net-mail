using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Snoopy.Microservice.Repositories;

namespace weesky.Snoopy.Microservice.Authentication.Authorization;

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

        if (!string.IsNullOrWhiteSpace(name) &&
            !string.IsNullOrWhiteSpace(domain) &&
            await adminRepository.IsAdminAsync(name, domain))
        {
            context.Succeed(requirement);
        }
    }
}
