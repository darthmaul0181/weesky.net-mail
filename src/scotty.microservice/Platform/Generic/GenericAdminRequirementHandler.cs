using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using weesky.Scotty.Microservice.Authentication.Authorization;

namespace weesky.Scotty.Microservice.Platform.Generic;

internal sealed class GenericAdminRequirementHandler(GenericAdministrators administrators)
    : AuthorizationHandler<AdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminRequirement requirement)
    {
        var name = context.User.FindFirst(ClaimTypes.Upn)?.Value;
        var domain = context.User.FindFirst(ClaimTypes.Dns)?.Value;

        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(domain) &&
            administrators.Contains($"{name}@{domain}"))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
